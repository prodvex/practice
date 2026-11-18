#!/usr/bin/env python3
"""Offline, dependency-free structural checks for GamingClub (NO app compilation)."""
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []
checks = 0


def check(condition: bool, description: str) -> None:
    global checks
    checks += 1
    if condition:
        print("[OK]   " + description)
    else:
        print("[FAIL] " + description)
        errors.append(description)


def read(path: str) -> str:
    file = ROOT / path
    if not file.is_file():
        check(False, f"Required file: {path}")
        return ""
    return file.read_text(encoding="utf-8")


required = [
    "README.md",
    "database/schema.sql",
    "database/schema-integration-tests.sql",
    "database/migrations/001_void_pending_payments.sql",
    "src/GamingClub.API/Program.cs",
    "src/GamingClub.API/CancellationStore.cs",
    "src/GamingClub.Desktop/MainWindow.xaml",
    "tests/GamingClub.Tests/SqlBookingIntegrationTests.cs",
    "docs/er-diagram.md",
    "docs/design/prototype.html",
]
for path in required:
    check((ROOT / path).is_file(), f"File exists: {path}")

# The Android Studio source is intentionally on feature/android-client until PR #1
# has been reviewed. A main-branch audit must not fail on an unmerged feature.
android = ROOT / "mobile/GamingClub.Android/app/src/main/java/com/gamingclub/client/MainActivity.kt"
if android.exists():
    check("class MainActivity" in android.read_text(encoding="utf-8"),
          "Android activity source present")
else:
    print("[SKIP] Android source is expected on feature/android-client (draft PR #1).")

screen_names = [
    "01-login.svg",
    "02-hall.svg",
    "03-booking.svg",
    "04-my-bookings.svg",
    "05-session.svg",
    "06-profile.svg",
]
prototype = read("docs/design/prototype.html")
for name in screen_names:
    svg = ROOT / "docs" / "design" / name
    check(svg.is_file(), f"Screen SVG exists: {name}")
    check(name in prototype, f"Clickable prototype refers to: {name}")

# Well-formedness catches a common WPF/Android/SVG editing failure without
# requiring Visual Studio, Android Studio, NuGet or a browser.
xml_paths = list((ROOT / "src").rglob("*.xaml"))
xml_paths += list((ROOT / "src").rglob("*.csproj"))
xml_paths += list((ROOT / "tests").rglob("*.csproj"))
xml_paths += list((ROOT / "mobile").rglob("AndroidManifest.xml"))
xml_paths += [ROOT / "docs" / "design" / name for name in screen_names]
for file in xml_paths:
    if not file.exists():
        check(False, "XML file missing: " + str(file.relative_to(ROOT)))
        continue
    try:
        ET.parse(file)
    except ET.ParseError as exc:
        check(False, f"XML malformed: {file.relative_to(ROOT)}: {exc}")
    else:
        check(True, f"XML well-formed: {file.relative_to(ROOT)}")

schema = read("database/schema.sql")
for table in ["Roles", "Users", "Halls", "Seats", "Tariffs", "Bookings",
              "Sessions", "Payments"]:
    check(f"CREATE TABLE dbo.{table}" in schema, f"SQL table: {table}")

api = read("src/GamingClub.API/Program.cs")
for route in ["/api/auth/register", "/api/auth/login", "/api/bookings",
              "/api/sessions/", "/api/admin"]:
    check(route in api, f"API route declaration: {route}")
check("RequireRateLimiting" in api, "Auth route limiter declared")
check("RequireAuthorization" in api, "Authenticated routes declared")

print()
print(f"Static checks: {checks - len(errors)}/{checks} passed.")
print("Not checked: C#/Kotlin compilation, SQL execution, runtime behavior, coverage.")
sys.exit(1 if errors else 0)
