"""Exercise the real PowerShell deletion gate in isolated repository fixtures.

Run with Python 3, ripgrep (PCRE2 enabled), and pwsh on PATH.
MCSL_PWSH can select a portable PowerShell executable without changing PATH.
"""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


GATE = Path(__file__).resolve().parents[1] / "VerifyNoV1Runtime.ps1"
REGISTRY = "src/MCServerLauncher.WPF/Features/Plugins/PluginRegistryClient.cs"
DAEMON = "src/MCServerLauncher.Daemon/Remote/Endpoint.cs"
# Assemble prohibited tokens so this regression runner is not itself a violation.
OLD_ENDPOINT = "/api/" + "v1"
OLD_TYPE = "Action" + "Response"


class V1DeletionGateTests(unittest.TestCase):
    def setUp(self):
        self.pwsh = os.environ.get("MCSL_PWSH") or shutil.which("pwsh")
        if not self.pwsh:
            self.fail("PowerShell is required: put pwsh on PATH or set MCSL_PWSH")
        if not shutil.which("rg"):
            self.fail("ripgrep with PCRE2 support is required")
        fixture = tempfile.TemporaryDirectory(prefix="mcsl-v1-gate-")
        self.addCleanup(fixture.cleanup)
        self.root = Path(fixture.name)
        self.put("MCServerLauncher.slnx", "<Solution />")
        self.put("tools/VerifyNoV1Runtime.ps1", GATE.read_text(encoding="utf-8"))

    def put(self, relative_path, content):
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def assert_gate(self, passes):
        result = subprocess.run(
            [self.pwsh, "-NoProfile", "-File", str(self.root / "tools/VerifyNoV1Runtime.ps1")],
            cwd=self.root.parent,  # The gate must resolve paths relative to its own repository.
            capture_output=True,
            text=True,
        )
        output = result.stdout + result.stderr
        if passes:
            self.assertEqual(0, result.returncode, output)
            self.assertIn("V1 deletion gate passed", output)
        else:
            self.assertNotEqual(0, result.returncode, output)
            # A tool/syntax error must not count as detecting a forbidden reference.
            self.assertIn("V1 deletion gate failed:", output)

    def test_clean_v2_runtime_passes(self):
        self.put(DAEMON, 'const string Endpoint = "/api/v2";')
        self.assert_gate(True)

    def test_registry_list_detail_update_and_download_routes_pass(self):
        routes = [
            "/plugins?pageSize=50",
            "/plugins/{id}",
            "/plugins/{id}/updates?from={version}",
            "/plugins/{id}/versions/{version}/download",
            "/plugins",
        ]
        self.put(REGISTRY, "\n".join(f'var uri = $"{{BaseUrl}}{OLD_ENDPOINT}{route}";' for route in routes))
        self.assert_gate(True)

    def test_old_endpoint_in_daemon_fails(self):
        self.put(DAEMON, f'const string Endpoint = "{OLD_ENDPOINT}";')
        self.assert_gate(False)

    def test_registry_route_in_another_file_is_not_exempt(self):
        self.put(DAEMON, f'var uri = "{OLD_ENDPOINT}/plugins";')
        self.assert_gate(False)

    def test_old_daemon_endpoint_in_registry_client_fails(self):
        self.put(REGISTRY, f'var uri = "{OLD_ENDPOINT}";')
        self.assert_gate(False)

    def test_other_v1_route_in_registry_client_fails(self):
        self.put(REGISTRY, f'var uri = "{OLD_ENDPOINT}/instances";')
        self.assert_gate(False)

    def test_similar_registry_route_is_not_exempt(self):
        self.put(REGISTRY, f'var uri = "{OLD_ENDPOINT}/plugins-old";')
        self.assert_gate(False)

    def test_allowed_and_forbidden_routes_on_same_line_fail(self):
        self.put(REGISTRY, f'var allowed = "{OLD_ENDPOINT}/plugins"; var forbidden = "{OLD_ENDPOINT}";')
        self.assert_gate(False)

    def test_legacy_types_in_registry_client_still_fail(self):
        self.put(REGISTRY, f'{OLD_TYPE} response; var uri = "{OLD_ENDPOINT}/plugins";')
        self.assert_gate(False)

    def test_legacy_types_in_daemon_fail(self):
        self.put(DAEMON, f'{OLD_TYPE} response;')
        self.assert_gate(False)

    def test_readme_old_endpoint_fails(self):
        self.put("README.md", f"Connect to {OLD_ENDPOINT}.")
        self.assert_gate(False)

    def test_restored_legacy_runtime_file_fails(self):
        self.put("src/MCServerLauncher.Daemon/Remote/Ws" + "ActionPlugin.cs", "// restored")
        self.assert_gate(False)


if __name__ == "__main__":
    unittest.main(verbosity=2)
