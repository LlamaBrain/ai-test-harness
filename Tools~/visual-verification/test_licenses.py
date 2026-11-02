import json
import pathlib
import subprocess
import sys
import tempfile
import unittest


class LicenseInventoryTests(unittest.TestCase):
    def run_inventory(self, extra=None, omit_notice=None):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            installed = root / 'installed'
            (installed / 'vcpkg').mkdir(parents=True)
            names = ['opencv4', 'nlohmann-json', 'libpng', 'zlib'] + ([extra] if extra else [])
            paragraphs = []
            for name in names:
                paragraphs.append(f'Package: {name}\nVersion: 1.0\nArchitecture: x64-linux\nStatus: install ok installed')
                notice = installed / 'x64-linux' / 'share' / name / 'copyright'
                notice.parent.mkdir(parents=True)
                if name != omit_notice:
                    notice.write_text('Test notice')
            (installed / 'vcpkg' / 'status').write_text('\n\n'.join(paragraphs))
            output = root / 'notices'
            run = subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name('licenses.py')),
                                  str(installed), str(output)], capture_output=True, text=True)
            inventory = json.loads((output / 'inventory.json').read_text()) if run.returncode == 0 else None
            return run, inventory

    def test_copies_every_notice_and_records_digest(self):
        run, inventory = self.run_inventory()
        self.assertEqual(run.returncode, 0, run.stderr)
        self.assertEqual(len(inventory['dependencies']), 4)
        self.assertTrue(all(len(item['noticeSha256']) == 64 for item in inventory['dependencies']))

    def test_video_dependency_rejected(self):
        run, _ = self.run_inventory(extra='ffmpeg')
        self.assertNotEqual(run.returncode, 0)
        self.assertIn('Video dependency prohibited', run.stderr)

    def test_missing_notice_rejected(self):
        run, _ = self.run_inventory(omit_notice='libpng')
        self.assertNotEqual(run.returncode, 0)
        self.assertIn('Missing redistribution notice', run.stderr)


if __name__ == '__main__':
    unittest.main()
