"""
Port discovery utility for Unity MCP Server.
Reads port configuration saved by Unity Bridge.
"""

import json
import os
import logging
from pathlib import Path
from typing import Optional

logger = logging.getLogger("unity-mcp-server")

class PortDiscovery:
    """Handles port discovery from Unity Bridge registry"""
    
    REGISTRY_FILE = "unity-mcp-port.json"
    
    @staticmethod
    def get_registry_path() -> Path:
        """Get the path to the port registry file"""
        return Path.home() / ".unity-mcp" / PortDiscovery.REGISTRY_FILE
    
    @staticmethod
    def discover_unity_port() -> int:
        """
        Discover Unity port from registry file with fallback to default
        
        Returns:
            Port number to connect to
        """
        registry_file = PortDiscovery.get_registry_path()
        
        if registry_file.exists():
            try:
                with open(registry_file, 'r') as f:
                    port_config = json.load(f)
                
                unity_port = port_config.get('unity_port')
                if unity_port and isinstance(unity_port, int):
                    logger.info(f"Discovered Unity port from registry: {unity_port}")
                    return unity_port
                    
            except Exception as e:
                logger.warning(f"Could not read port registry: {e}")
        
        # Fallback to default port
        logger.info("No port registry found, using default port 6400")
        return 6400
    
    @staticmethod
    def get_port_config() -> Optional[dict]:
        """
        Get the full port configuration from registry
        
        Returns:
            Port configuration dict or None if not found
        """
        registry_file = PortDiscovery.get_registry_path()
        
        if not registry_file.exists():
            return None
            
        try:
            with open(registry_file, 'r') as f:
                return json.load(f)
        except Exception as e:
            logger.warning(f"Could not read port configuration: {e}")
            return None