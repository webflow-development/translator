"""Select an unexpired App Store profile whose certificate has a local private key."""
import datetime
import hashlib
import pathlib
import plistlib
import re
import shlex
import subprocess
import sys

identities = subprocess.check_output(["security", "find-identity", "-v", "-p", "codesigning"], text=True)
identity_names = {fingerprint.upper(): name for fingerprint, name in re.findall(r'([A-Fa-f0-9]{40}) "([^"]+)"', identities)}
profile_roots = [pathlib.Path.home() / "Library/MobileDevice/Provisioning Profiles",
                 pathlib.Path.home() / "Library/Developer/Xcode/UserData/Provisioning Profiles"]
matches = []
for root in profile_roots:
    for path in root.glob("*.mobileprovision"):
        decoded = subprocess.run(["security", "cms", "-D", "-i", str(path)], capture_output=True, check=False)
        if decoded.returncode:
            continue
        profile = plistlib.loads(decoded.stdout)
        entitlements = profile.get("Entitlements", {})
        app_id = entitlements.get("application-identifier", "")
        if app_id.split(".", 1)[-1] != "ch.paulo.koe":
            continue
        if entitlements.get("get-task-allow") or profile.get("ProvisionedDevices") or profile.get("ProvisionsAllDevices"):
            continue
        expiry = profile.get("ExpirationDate", datetime.datetime.min)
        if expiry <= datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None):
            continue
        for certificate in profile.get("DeveloperCertificates", []):
            fingerprint = hashlib.sha1(certificate).hexdigest().upper()
            if fingerprint in identity_names:
                matches.append((expiry, profile["UUID"], identity_names[fingerprint]))
if not matches:
    sys.exit("No usable App Store profile for ch.paulo.koe with a matching private key. Add signing files in Codemagic Code signing identities.")
_, profile_id, certificate_name = max(matches)
print("export KOE_SIGNING_PROFILE=" + shlex.quote(profile_id))
print("export KOE_SIGNING_CERTIFICATE=" + shlex.quote(certificate_name))
