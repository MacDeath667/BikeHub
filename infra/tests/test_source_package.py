import importlib.util
import os
from pathlib import Path
import tarfile
import tempfile
import unittest

MODULE = Path(__file__).parents[1] / "ansible/scripts/package-source.py"
spec = importlib.util.spec_from_file_location("package_source", MODULE)
package_source = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package_source)


class SourcePackageTests(unittest.TestCase):
    def test_context_includes_web_and_api_without_dependencies_or_secrets(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "repo"
            files = {
                ".dockerignore": "**/node_modules",
                ".editorconfig": "[*.cs]\ncsharp_prefer_braces = true\n",
                "Directory.Build.props": "<Project><PropertyGroup><EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild></PropertyGroup></Project>",
                "Web/package.json": '{}',
                "Web/src/main.tsx": "export {};",
                "src/BikeHub.Api/Program.cs": "app.Run();",
                "Web/node_modules/private/token": "do not package",
                "Web/dist/index.html": "generated",
                "Web/.env.local": "do not package",
                "src/BikeHub.Api/obj/cache": "generated",
                "infra/terraform/terraform.tfstate": "do not package",
                "infra/ansible/inventory/group_vars/bikehub/vault.yml": "do not package",
            }
            for name, content in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content)
            first = Path(temp) / "first.tar.gz"
            digest = package_source.package(root, first)
            with tarfile.open(first) as archive:
                names = archive.getnames()
            self.assertIn("Web/package.json", names)
            self.assertIn("src/BikeHub.Api/Program.cs", names)
            self.assertIn(".editorconfig", names)
            self.assertIn("Directory.Build.props", names)
            self.assertNotIn("infra", names)
            self.assertFalse(any("node_modules" in p or "/obj" in p or "/dist" in p or "/.env" in p for p in names))
            os.utime(root / "Web/package.json", (1, 1))
            second = Path(temp) / "second.tar.gz"
            self.assertEqual(digest, package_source.package(root, second))
            (root / "Web/package.json").write_text('{"version":"2"}')
            self.assertNotEqual(digest, package_source.package(root, second))

    def test_missing_web_fails_before_deploy(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "src").mkdir()
            (root / ".dockerignore").write_text("")
            with self.assertRaisesRegex(ValueError, "Web"):
                package_source.package(root, root / "context.tar.gz")


if __name__ == "__main__":
    unittest.main()
