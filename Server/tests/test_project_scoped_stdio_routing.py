import json
from datetime import datetime, timezone

import pytest

from core.config import config
from transport.legacy.port_discovery import PortDiscovery


@pytest.fixture(autouse=True)
def restore_project_scope():
    previous = config.project_path
    try:
        yield
    finally:
        config.project_path = previous


def _write_status(directory, hash_value, project_root, port):
    path = directory / f"unity-mcp-status-{hash_value}.json"
    path.write_text(
        json.dumps(
            {
                "unity_port": port,
                "reloading": False,
                "project_path": str(project_root / "Assets"),
                "last_heartbeat": datetime.now(timezone.utc).isoformat(),
            }
        ),
        encoding="utf-8",
    )


def _write_port(directory, hash_value, project_root, port):
    path = directory / f"unity-mcp-port-{hash_value}.json"
    path.write_text(
        json.dumps(
            {
                "unity_port": port,
                "project_path": str(project_root / "Assets"),
            }
        ),
        encoding="utf-8",
    )
    return path


def test_scoped_discovery_probes_only_the_selected_project(tmp_path, monkeypatch):
    project_a = tmp_path / "ProjectA"
    project_b = tmp_path / "ProjectB"
    project_a.mkdir()
    project_b.mkdir()
    _write_status(tmp_path, "aaaaaaaa", project_a, 6401)
    _write_status(tmp_path, "bbbbbbbb", project_b, 6402)

    config.project_path = PortDiscovery._normalize_project_root(str(project_a))
    monkeypatch.setattr(PortDiscovery, "get_registry_dir", staticmethod(lambda: tmp_path))
    probed_ports = []

    def probe(port):
        probed_ports.append(port)
        return True

    monkeypatch.setattr(PortDiscovery, "_try_probe_unity_mcp", staticmethod(probe))

    instances = PortDiscovery.discover_all_unity_instances()

    assert [instance.id for instance in instances] == ["ProjectA@aaaaaaaa"]
    assert probed_ports == [6401]


def test_scoped_port_candidates_exclude_other_projects(tmp_path, monkeypatch):
    project_a = tmp_path / "ProjectA"
    project_b = tmp_path / "ProjectB"
    project_a.mkdir()
    project_b.mkdir()
    expected = _write_port(tmp_path, "aaaaaaaa", project_a, 6401)
    _write_port(tmp_path, "bbbbbbbb", project_b, 6402)

    config.project_path = PortDiscovery._normalize_project_root(str(project_a))
    monkeypatch.setattr(PortDiscovery, "get_registry_dir", staticmethod(lambda: tmp_path))
    monkeypatch.setattr(
        PortDiscovery,
        "get_registry_path",
        staticmethod(lambda: tmp_path / PortDiscovery.REGISTRY_FILE),
    )

    assert PortDiscovery.list_candidate_files() == [expected]


def test_assets_path_and_project_root_match_the_same_scope(tmp_path):
    project = tmp_path / "Project"
    assets = project / "Assets"
    assets.mkdir(parents=True)
    config.project_path = PortDiscovery._normalize_project_root(str(project))

    assert PortDiscovery._matches_project_scope(str(project))
    assert PortDiscovery._matches_project_scope(str(assets))


def test_unscoped_discovery_preserves_all_instances(tmp_path, monkeypatch):
    project_a = tmp_path / "ProjectA"
    project_b = tmp_path / "ProjectB"
    project_a.mkdir()
    project_b.mkdir()
    _write_status(tmp_path, "aaaaaaaa", project_a, 6401)
    _write_status(tmp_path, "bbbbbbbb", project_b, 6402)

    config.project_path = None
    monkeypatch.setattr(PortDiscovery, "get_registry_dir", staticmethod(lambda: tmp_path))
    probed_ports = []

    def probe(port):
        probed_ports.append(port)
        return True

    monkeypatch.setattr(PortDiscovery, "_try_probe_unity_mcp", staticmethod(probe))

    instances = PortDiscovery.discover_all_unity_instances()

    assert {instance.id for instance in instances} == {
        "ProjectA@aaaaaaaa",
        "ProjectB@bbbbbbbb",
    }
    assert set(probed_ports) == {6401, 6402}
