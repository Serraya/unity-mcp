import socket
import json
import logging
from dataclasses import dataclass
from pathlib import Path
import time
import random
import errno
from typing import Dict, Any
from config import config
from port_discovery import PortDiscovery
import struct

# Configure logging using settings from config
logging.basicConfig(
    level=getattr(logging, config.log_level),
    format=config.log_format
)
logger = logging.getLogger("unity-mcp-server")

@dataclass
class UnityConnection:
    """Manages the socket connection to the Unity Editor."""
    host: str = config.unity_host
    port: int = None  # Will be set dynamically
    sock: socket.socket = None  # Socket for Unity communication
    
    def __post_init__(self):
        """Set port from discovery if not explicitly provided"""
        if self.port is None:
            self.port = PortDiscovery.discover_unity_port()

    def connect(self) -> bool:
        """Establish a connection to the Unity Editor."""
        if self.sock:
            return True
        try:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.connect((self.host, self.port))
            logger.info(f"Connected to Unity at {self.host}:{self.port}")
            return True
        except Exception as e:
            logger.error(f"Failed to connect to Unity: {str(e)}")
            self.sock = None
            return False

    def disconnect(self):
        """Close the connection to the Unity Editor."""
        if self.sock:
            try:
                self.sock.close()
            except Exception as e:
                logger.error(f"Error disconnecting from Unity: {str(e)}")
            finally:
                self.sock = None

    def receive_full_response(self, sock) -> bytes:
        """Receive a complete response from Unity using 8-byte length-prefixed framing, with legacy fallback."""
        sock.settimeout(config.connection_timeout)
        # Try framed first
        try:
            header = self._read_exact(sock, 8)
            (payload_len,) = struct.unpack('>Q', header)
            if 0 < payload_len <= (64 * 1024 * 1024):
                return self._read_exact(sock, payload_len)
            # Implausible length -> treat as legacy stream; fall through
            legacy_prefix = header
        except Exception:
            # Could not read header — treat as legacy
            legacy_prefix = b''

        # Legacy: read until parses as JSON or times out
        chunks: list[bytes] = []
        if legacy_prefix:
            chunks.append(legacy_prefix)
        while True:
            chunk = sock.recv(config.buffer_size)
            if not chunk:
                data = b''.join(chunks)
                if not data:
                    raise Exception("Connection closed before receiving data")
                return data
            chunks.append(chunk)
            data = b''.join(chunks)
            try:
                if data.strip() == b'ping':
                    return data
                json.loads(data.decode('utf-8'))
                return data
            except Exception:
                continue

    def _read_exact(self, sock: socket.socket, n: int) -> bytes:
        buf = bytearray(n)
        view = memoryview(buf)
        read = 0
        while read < n:
            r = sock.recv_into(view[read:])
            if r == 0:
                raise Exception("Connection closed during read")
            read += r
        return bytes(buf)

    def send_command(self, command_type: str, params: Dict[str, Any] = None) -> Dict[str, Any]:
        """Send a command with retry/backoff and port rediscovery. Pings only when requested."""
        # Defensive guard: catch empty/placeholder invocations early
        if not command_type:
            raise ValueError("MCP call missing command_type")
        if params is None:
            # Return a fast, structured error that clients can display without hanging
            return {"success": False, "error": "MCP call received with no parameters (client placeholder?)"}
        attempts = max(config.max_retries, 5)
        base_backoff = max(0.5, config.retry_delay)

        def read_status_file() -> dict | None:
            try:
                status_files = sorted(Path.home().joinpath('.unity-mcp').glob('unity-mcp-status-*.json'), key=lambda p: p.stat().st_mtime, reverse=True)
                if not status_files:
                    return None
                latest = status_files[0]
                with latest.open('r') as f:
                    return json.load(f)
            except Exception:
                return None

        last_short_timeout = None

        # Preflight: if Unity reports reloading, return a structured hint so clients can retry politely
        try:
            status = read_status_file()
            if status and (status.get('reloading') or status.get('reason') == 'reloading'):
                return {
                    "success": False,
                    "state": "reloading",
                    "retry_after_ms": int(config.reload_retry_ms),
                    "error": "Unity domain reload in progress",
                    "message": "Unity is reloading scripts; please retry shortly"
                }
        except Exception:
            pass

        for attempt in range(attempts + 1):
            try:
                # Ensure connected
                if not self.sock:
                    # During retries use short connect timeout
                    self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                    self.sock.settimeout(1.0)
                    self.sock.connect((self.host, self.port))
                    # restore steady-state timeout for receive
                    self.sock.settimeout(config.connection_timeout)
                    logger.info(f"Connected to Unity at {self.host}:{self.port}")

                # Build payload
                if command_type == 'ping':
                    body = b'ping'
                else:
                    command = {"type": command_type, "params": params or {}}
                    body = json.dumps(command, ensure_ascii=False).encode('utf-8')

                # Send with 8-byte big-endian length prefix for robustness
                header = struct.pack('>Q', len(body))
                self.sock.sendall(header + body)

                # During retry bursts use a short receive timeout
                if attempt > 0 and last_short_timeout is None:
                    last_short_timeout = self.sock.gettimeout()
                    self.sock.settimeout(1.0)
                response_data = self.receive_full_response(self.sock)
                # restore steady-state timeout if changed
                if last_short_timeout is not None:
                    self.sock.settimeout(config.connection_timeout)
                    last_short_timeout = None

                # Parse
                if command_type == 'ping':
                    resp = json.loads(response_data.decode('utf-8'))
                    if resp.get('status') == 'success' and resp.get('result', {}).get('message') == 'pong':
                        return {"message": "pong"}
                    raise Exception("Ping unsuccessful")

                resp = json.loads(response_data.decode('utf-8'))
                if resp.get('status') == 'error':
                    err = resp.get('error') or resp.get('message', 'Unknown Unity error')
                    raise Exception(err)
                return resp.get('result', {})
            except Exception as e:
                logger.warning(f"Unity communication attempt {attempt+1} failed: {e}")
                try:
                    if self.sock:
                        self.sock.close()
                finally:
                    self.sock = None

                # Re-discover port each time
                try:
                    new_port = PortDiscovery.discover_unity_port()
                    if new_port != self.port:
                        logger.info(f"Unity port changed {self.port} -> {new_port}")
                    self.port = new_port
                except Exception as de:
                    logger.debug(f"Port discovery failed: {de}")

                if attempt < attempts:
                    # Heartbeat-aware, jittered backoff
                    status = read_status_file()
                    # Base exponential backoff
                    backoff = base_backoff * (2 ** attempt)
                    # Decorrelated jitter multiplier
                    jitter = random.uniform(0.1, 0.3)

                    # Fast‑retry for transient socket failures
                    fast_error = isinstance(e, (ConnectionRefusedError, ConnectionResetError, TimeoutError))
                    if not fast_error:
                        try:
                            err_no = getattr(e, 'errno', None)
                            fast_error = err_no in (errno.ECONNREFUSED, errno.ECONNRESET, errno.ETIMEDOUT)
                        except Exception:
                            pass

                    # Cap backoff depending on state
                    if status and status.get('reloading'):
                        cap = 0.8
                    elif fast_error:
                        cap = 0.25
                    else:
                        cap = 3.0

                    sleep_s = min(cap, jitter * (2 ** attempt))
                    time.sleep(sleep_s)
                    continue
                raise

# Global Unity connection
_unity_connection = None

def get_unity_connection() -> UnityConnection:
    """Retrieve or establish a persistent Unity connection."""
    global _unity_connection
    if _unity_connection is not None:
        try:
            # Try to ping with a short timeout to verify connection
            result = _unity_connection.send_command("ping")
            # If we get here, the connection is still valid
            logger.debug("Reusing existing Unity connection")
            return _unity_connection
        except Exception as e:
            logger.warning(f"Existing connection failed: {str(e)}")
            try:
                _unity_connection.disconnect()
            except:
                pass
            _unity_connection = None
    
    # Create a new connection
    logger.info("Creating new Unity connection")
    _unity_connection = UnityConnection()
    if not _unity_connection.connect():
        _unity_connection = None
        raise ConnectionError("Could not connect to Unity. Ensure the Unity Editor and MCP Bridge are running.")
    
    try:
        # Verify the new connection works
        _unity_connection.send_command("ping")
        logger.info("Successfully established new Unity connection")
        return _unity_connection
    except Exception as e:
        logger.error(f"Could not verify new connection: {str(e)}")
        try:
            _unity_connection.disconnect()
        except:
            pass
        _unity_connection = None
        raise ConnectionError(f"Could not establish valid Unity connection: {str(e)}") 


# -----------------------------
# Centralized retry helpers
# -----------------------------

def _is_reloading_response(resp: dict) -> bool:
    """Return True if the Unity response indicates the editor is reloading."""
    if not isinstance(resp, dict):
        return False
    if resp.get("state") == "reloading":
        return True
    message_text = (resp.get("message") or resp.get("error") or "").lower()
    return "reload" in message_text


def send_command_with_retry(command_type: str, params: Dict[str, Any], *, max_retries: int | None = None, retry_ms: int | None = None) -> Dict[str, Any]:
    """Send a command via the shared connection, waiting politely through Unity reloads.

    Uses config.reload_retry_ms and config.reload_max_retries by default. Preserves the
    structured failure if retries are exhausted.
    """
    conn = get_unity_connection()
    if max_retries is None:
        max_retries = getattr(config, "reload_max_retries", 40)
    if retry_ms is None:
        retry_ms = getattr(config, "reload_retry_ms", 250)

    response = conn.send_command(command_type, params)
    retries = 0
    while _is_reloading_response(response) and retries < max_retries:
        delay_ms = int(response.get("retry_after_ms", retry_ms)) if isinstance(response, dict) else retry_ms
        time.sleep(max(0.0, delay_ms / 1000.0))
        retries += 1
        response = conn.send_command(command_type, params)
    return response


async def async_send_command_with_retry(command_type: str, params: Dict[str, Any], *, loop=None, max_retries: int | None = None, retry_ms: int | None = None) -> Dict[str, Any]:
    """Async wrapper that runs the blocking retry helper in a thread pool."""
    try:
        import asyncio  # local import to avoid mandatory asyncio dependency for sync callers
        if loop is None:
            loop = asyncio.get_running_loop()
        return await loop.run_in_executor(
            None,
            lambda: send_command_with_retry(command_type, params, max_retries=max_retries, retry_ms=retry_ms),
        )
    except Exception as e:
        # Return a structured error dict for consistency with other responses
        return {"success": False, "error": f"Python async retry helper failed: {str(e)}"}
