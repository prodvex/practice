"""Запуск GamingClub на Windows x64. Управление Android основано на примере МФЦ."""
from argparse import ArgumentParser
from contextlib import ExitStack, contextmanager
import ctypes
from ctypes import wintypes
from hashlib import file_digest, sha256
from pathlib import Path
from shutil import copytree, rmtree, which
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, ProxyHandler, build_opener
from uuid import uuid4
from zipfile import ZipFile
import json
import msvcrt
import os
import platform
import re
import secrets
import socket
import subprocess
import sys
import tempfile
from time import monotonic, sleep


ROOT = Path(__file__).resolve().parent
APK = ROOT / "builds/android/GamingClub.apk"
WPF = ROOT / "builds/windows/desktop/GamingClub.Desktop.exe"
API = ROOT / "builds/windows/api/GamingClub.API.exe"
LOCALDB_URL = "https://download.microsoft.com/download/3/8/d/38de7036-2433-4207-8eae-06e247e17b25/SqlLocalDB.msi"
LOCALDB_SHA = "224d483992ef60368dac70cea174dcfaf43a3ca06ada331c67dc6119a26490f6"
SYSTEM_IMAGE = "system-images;android-35;google_apis;x86_64"
MIN_ANDROID_API = 30
AVD = "GamingClub_API35"
SDK_URL = "https://dl.google.com/android/repository/commandlinetools-win-15859902_latest.zip"
SDK_SHA = "90ae805d20434428bffcb699c290860f19bb5f66a67e6b330067e3de801fb04a"
JDK_URL = "https://aka.ms/download-jdk/microsoft-jdk-21.0.13.0.2-windows-x64.zip"
JDK_SHA = "c5706a9cf482ef1a0622a86eea5b13d557b07f04554572af061e2d02b58c0f99"
LOCAL_HTTP = build_opener(ProxyHandler({}))
POWERSHELL = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe"


class RestartRequired(RuntimeError):
    pass


def own_process_tree():
    """Windows завершает потомков при закрытии последнего дескриптора задания."""
    class BasicLimits(ctypes.Structure):
        _fields_ = [
            ("PerProcessUserTimeLimit", ctypes.c_int64), ("PerJobUserTimeLimit", ctypes.c_int64),
            ("LimitFlags", wintypes.DWORD), ("MinimumWorkingSetSize", ctypes.c_size_t),
            ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", wintypes.DWORD),
            ("Affinity", ctypes.c_size_t), ("PriorityClass", wintypes.DWORD),
            ("SchedulingClass", wintypes.DWORD),
        ]

    class ExtendedLimits(ctypes.Structure):
        _fields_ = [
            ("BasicLimitInformation", BasicLimits), ("IoCounters", ctypes.c_uint64 * 6),
            ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
            ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t),
        ]

    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateJobObjectW.argtypes = (ctypes.c_void_p, wintypes.LPCWSTR)
    kernel.CreateJobObjectW.restype = wintypes.HANDLE
    kernel.SetInformationJobObject.argtypes = (
        wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD,
    )
    kernel.AssignProcessToJobObject.argtypes = (wintypes.HANDLE, wintypes.HANDLE)
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)

    job = kernel.CreateJobObjectW(None, None)
    if not job:
        raise ctypes.WinError(ctypes.get_last_error())
    limits = ExtendedLimits()
    limits.BasicLimitInformation.LimitFlags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    if (not kernel.SetInformationJobObject(job, 9, ctypes.byref(limits), ctypes.sizeof(limits))
            or not kernel.AssignProcessToJobObject(job, kernel.GetCurrentProcess())):
        error = ctypes.get_last_error()
        kernel.CloseHandle(job)
        raise ctypes.WinError(error)
    # Keep the non-inheritable handle open until the launcher itself exits.


class HttpsRedirect(HTTPRedirectHandler):
    """Разрешить перенаправление на HTTPS при загрузке закреплённого архива."""
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if not newurl.startswith("https://"):
            raise URLError("Перенаправление загрузки допускается только по HTTPS")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def download(url, digest, target):
    print("Загрузка:", url, flush=True)
    opener = build_opener(HttpsRedirect())
    fingerprint = sha256()
    with opener.open(url, timeout=90) as response:
        for chunk in iter(lambda: response.read(1024 * 1024), b""):
            fingerprint.update(chunk)
            target.write(chunk)
    if fingerprint.hexdigest() != digest:
        raise RuntimeError("Контрольная сумма загрузки не совпала.")


def fetch_zip(url, digest, directory):
    directory.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryFile(dir=directory.parent) as target:
        download(url, digest, target)
        with ZipFile(target) as archive:
            archive.extractall(directory)


@contextmanager
def launcher_lock(state):
    lockfile = state / "launcher.lock"
    with lockfile.open("a+b") as lock:
        if lock.tell() == 0:
            lock.write(b"\0")
            lock.flush()
        lock.seek(0)
        try:
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError as exc:
            raise RuntimeError("Эта распакованная копия GamingClub уже запущена.") from exc
        try:
            yield
        finally:
            lock.seek(0)
            msvcrt.locking(lock.fileno(), msvcrt.LK_UNLCK, 1)


def stop(process):
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=8)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()


def start(stack, command, state, name, env, hidden=False):
    logs = [stack.enter_context((state / f"{name}.{kind}.log").open("wb")) for kind in ("out", "err")]
    process = subprocess.Popen(
        command, cwd=ROOT, env=env, stdin=subprocess.DEVNULL,
        stdout=logs[0], stderr=logs[1],
        creationflags=subprocess.CREATE_NO_WINDOW if hidden else 0,
    )
    stack.callback(stop, process)
    return process


def active_api(stack, state, env):
    portfile = state / "api-port.txt"
    for path in (portfile, state / "api-port.txt.tmp"):
        path.unlink(missing_ok=True)
        stack.callback(path.unlink, missing_ok=True)
    local = env | {"GAMINGCLUB_PORT_FILE": str(portfile), "ASPNETCORE_ENVIRONMENT": "LocalDemo",
                   "DOTNET_ENVIRONMENT": "LocalDemo"}
    server = start(stack, [str(API)], state, "server", local, hidden=True)
    until = monotonic() + 45
    while monotonic() < until and server.poll() is None:
        if portfile.is_file():
            value = portfile.read_text(encoding="ascii").strip()
            if value.isascii() and value.isdigit():
                port = int(value)
                base = f"http://127.0.0.1:{port}"
                try:
                    with LOCAL_HTTP.open(base + "/health", timeout=1) as response:
                        if json.load(response) == {
                            "service": "GamingClub.API", "status": "ok", "variant": 18, "seed": 241810,
                        }:
                            return base, port
                except (OSError, HTTPError, URLError, ValueError):
                    pass
        sleep(.25)
    raise RuntimeError("API не прошёл проверку готовности. См. .local/server.out.log и server.err.log.")


def confirm(question):
    while True:
        answer = input(f"{question} [Д/Н]: ").strip().casefold()
        if answer in {"д", "да", "y", "yes"}:
            return True
        if answer in {"н", "нет", "n", "no"}:
            return False
        print("Введите Д или Н, затем нажмите Enter.")


def system_setup(action, extra_env=None):
    command = [str(POWERSHELL), "-NoProfile", "-ExecutionPolicy", "Bypass",
               "-File", str(ROOT / "scripts/Configure-GamingClubHost.ps1"), "-Action", action]
    return subprocess.run(command, env=os.environ | (extra_env or {})).returncode


def localdb_tool():
    programs = Path(os.environ.get("ProgramW6432") or os.environ["ProgramFiles"])
    tools = (programs / "Microsoft SQL Server").glob("*/Tools/Binn/SqlLocalDB.exe")
    path = next((candidate for candidate in sorted(tools, reverse=True)
                 if candidate.parents[2].name in {"150", "160", "170"}), None)
    return path


def provision_localdb(state):
    tool = localdb_tool()
    if tool is not None:
        return tool
    print("Условия Microsoft: https://www.microsoft.com/en-us/useterms")
    if not confirm("Принять условия и установить SQL Server Express LocalDB и Visual C++, если он нужен?"):
        raise RuntimeError("Для запуска базы требуется SQL Server Express LocalDB.")
    package = state / "SqlLocalDB.msi"
    if package.is_file():
        with package.open("rb") as existing:
            valid = file_digest(existing, "sha256").hexdigest() == LOCALDB_SHA
    else:
        valid = False
    if not valid:
        temporary = state / "SqlLocalDB.msi.download"
        try:
            with temporary.open("wb") as target:
                download(LOCALDB_URL, LOCALDB_SHA, target)
            temporary.replace(package)
        finally:
            temporary.unlink(missing_ok=True)
    result = system_setup("InstallLocalDB", {"GAMINGCLUB_LOCALDB_MSI": str(package)})
    if result in {1641, 3010}:
        raise RestartRequired("Для установки компонентов нужна перезагрузка Windows. Затем снова откройте Start-GamingClub.cmd.")
    tool = localdb_tool()
    if result != 0 or tool is None:
        raise RuntimeError(f"Установка LocalDB завершилась с кодом {result}. См. .local/localdb-install.log или vc-redist.log.")
    return tool


def provision_database(stack, state, env):
    tool = provision_localdb(state)
    instance = "GC" + sha256(str(state).casefold().encode("utf-8")).hexdigest()[:14]
    print("Запускаю отдельную базу SQL Server LocalDB.", flush=True)
    with (state / "localdb.log").open("wb") as log:
        inventory = subprocess.check_output([tool, "info"], stderr=log, timeout=30)
        if instance.encode("ascii").lower() not in inventory.lower().split():
            subprocess.run([tool, "create", instance], stdout=log, stderr=log, check=True, timeout=120)
        subprocess.run([tool, "start", instance], stdout=log, stderr=log, check=True, timeout=120)
        details = subprocess.check_output([tool, "info", instance], stderr=log, timeout=30)
        log.write(details)
        if b"np:" not in details:
            raise RuntimeError("LocalDB не выдала адрес запущенного экземпляра. См. .local/localdb.log.")
    stack.callback(subprocess.run, [tool, "stop", instance], stdout=subprocess.DEVNULL,
                   stderr=subprocess.DEVNULL, timeout=30)
    env = env | {
        "ConnectionStrings__GamingClub": f"Server=(localdb)\\{instance};Database=GamingClub;"
                                         "Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15",
        "GAMINGCLUB_JWT_SECRET": secrets.token_urlsafe(40),
    }
    admin_password, visitor_password = (secrets.token_urlsafe(14) for _ in range(2))
    setup = env | {"GAMINGCLUB_DEMO_ADMIN_PASSWORD": admin_password,
                   "GAMINGCLUB_DEMO_VISITOR_PASSWORD": visitor_password}
    process = start(stack, [str(API), "--initialize-demo", str(ROOT / "database")],
                    state, "database", setup, hidden=True)
    if process.wait(timeout=180) != 0:
        raise RuntimeError("Инициализация базы завершилась ошибкой. См. .local/database.err.log.")
    return env, admin_password, visitor_password


def jdk(home):
    if not home:
        return None
    home = Path(home)
    manifest = home / "release"
    if manifest.is_file() and (home / "bin/java.exe").is_file():
        version = re.search(r'^JAVA_VERSION="(\d+)', manifest.read_text(encoding="utf-8"), re.M)
        if version and int(version.group(1)) >= 17:
            return home
    return None


def sdk_complete(sdk):
    return sdk_cli(sdk) is not None and all((sdk / file).is_file() for file in (
        "emulator/emulator.exe", "platform-tools/adb.exe", "system-images/android-35/google_apis/x86_64/system.img",
    ))


def sdk_cli(sdk):
    tools = sdk / "cmdline-tools"
    candidates = [tools / "latest", *sorted(tools.glob("*"), reverse=True)]
    return next((cli for cli in candidates if all(
        (cli / "lib" / f"{name}-classpath.jar").is_file()
        for name in ("avdmanager", "sdkmanager")
    )), None)


def sdk_java(java, sdk, action, *arguments, env, choices=None):
    cli = sdk_cli(sdk)
    if cli is None:
        raise RuntimeError("Не найдены Android Command-Line Tools.")
    main, prop = (
        ("com.android.sdklib.tool.sdkmanager.SdkManagerCli", "com.android.sdklib.toolsdir")
        if action == "sdkmanager" else
        ("com.android.sdklib.tool.AvdManagerCli", "com.android.sdkmanager.toolsdir")
    )
    cmd = [str(java / "bin/java.exe"), f"-D{prop}={cli}", "-classpath",
           str(cli / "lib" / f"{action}-classpath.jar"), main, *arguments]
    subprocess.run(cmd, env=env, input=choices, text=True, check=True)


def sdk_environment(sdk, env):
    return env | {"ANDROID_HOME": str(sdk), "ANDROID_SDK_ROOT": str(sdk)}


def avd_properties(file):
    if not file.is_file():
        return {}
    return dict(line.split("=", 1) for line in file.read_text(
        encoding="utf-8-sig", errors="replace"
    ).splitlines() if "=" in line)


def installed_avd(state, sdk, env):
    if not (sdk / "emulator/emulator.exe").is_file() or not (sdk / "platform-tools/adb.exe").is_file():
        return None
    user = Path(env.get("ANDROID_USER_HOME") or (Path.home() / ".android"))
    emulator_home = Path(env.get("ANDROID_EMULATOR_HOME") or user)
    homes = [Path(env.get("ANDROID_AVD_HOME") or (emulator_home / "avd")),
             user / "avd", Path.home() / ".android/avd"]
    candidates = []
    for home in dict.fromkeys(homes):
        for ini in sorted(home.glob("*.ini")):
            record = avd_properties(ini)
            api = re.fullmatch(r"android-(\d+)", record.get("target", ""))
            if not api or int(api[1]) < MIN_ANDROID_API:
                continue
            directory = Path(record.get("path") or (home / f"{ini.stem}.avd"))
            if not directory.is_dir() and record.get("path.rel"):
                directory = home.parent / record["path.rel"]
            config = avd_properties(directory / "config.ini")
            image = config.get("image.sysdir.1")
            if (image and config.get("abi.type") in {"x86", "x86_64"}
                    and (sdk / image / "system.img").is_file()):
                candidates.append((int(api[1]), ini.stem, config))
    if not candidates:
        return None
    api, source, config = max(candidates, key=lambda candidate: candidate[0])
    name = f"GamingClub_Installed_API{api}"
    avd_home = state / "avd"
    directory = avd_home / f"{name}.avd"
    directory.mkdir(parents=True, exist_ok=True)
    profile = {key: value for key, value in config.items()
               if key.startswith(("hw.", "tag.")) or key in {"abi.type", "image.sysdir.1"}}
    profile.update({"AvdId": name, "avd.ini.displayname": "MFC", "disk.dataPartition.size": "2G",
                    "hw.sdCard": "no", "target": f"android-{api}"})
    (directory / "config.ini").write_text(
        "".join(f"{key}={value}\n" for key, value in profile.items()), encoding="utf-8")
    (avd_home / f"{name}.ini").write_text(
        f"avd.ini.encoding=UTF-8\npath={directory}\ntarget=android-{api}\n", encoding="utf-8")
    print(f"Использую образ Android из установленного эмулятора {source}.", flush=True)
    return name, project_android_environment(state, sdk, env)


def project_android_environment(state, sdk, env):
    avd_home = state / "avd"
    android_home = state / "android-user"
    avd_home.mkdir(exist_ok=True)
    android_home.mkdir(exist_ok=True)
    return sdk_environment(sdk, env) | {
        "ANDROID_USER_HOME": str(android_home), "ANDROID_AVD_HOME": str(avd_home),
        "ANDROID_EMULATOR_HOME": str(android_home),
    }


def accept_sdk_terms(state):
    accepted = state / "android-sdk-terms-accepted"
    if accepted.is_file():
        return
    print("Для установки Android SDK нужны интернет и несколько ГБ свободного места.")
    print("Условия Android SDK: https://developer.android.com/studio#terms-and-conditions")
    if not confirm("Принять условия и установить Android SDK?"):
        raise RuntimeError("Установка Android SDK отменена пользователем.")
    accepted.touch()


def provision_android(state, explicit, env):
    managed = state / "sdk"
    localapp = env.get("LOCALAPPDATA")
    defaults = [env.get("ANDROID_HOME"), env.get("ANDROID_SDK_ROOT"),
                Path(localapp) / "Android/Sdk" if localapp else None]
    candidates = [explicit] if explicit else list(dict.fromkeys(Path(p) for p in defaults if p))
    existing = next((sdk for sdk in candidates if sdk_complete(sdk)), None)
    if not existing and not (not explicit and sdk_complete(managed)):
        for sdk in candidates:
            avd = installed_avd(state, sdk, env)
            if avd:
                name, android_env = avd
                return sdk, name, android_env
    if explicit and not existing:
        raise RuntimeError("В указанном SDK нужен готовый AVD с Android 11+ или инструменты и образ Android 35 Google APIs x86_64. Для автоустановки запустите без -AndroidSdk.")
    sdk = existing or managed
    wants_sdk = not sdk_complete(sdk)
    avd_home = state / "avd"
    avd_exists = (avd_home / f"{AVD}.ini").is_file() and (avd_home / f"{AVD}.avd/config.ini").is_file()
    if wants_sdk:
        accept_sdk_terms(state)
    if wants_sdk or not avd_exists:
        java_root = state / "java"
        java_exe = which("java.exe", path=env.get("PATH", ""))
        java_homes = [
            env.get("JAVA_HOME"),
            *java_root.glob("*"),
            Path(env["ProgramFiles"]) / "Android/Android Studio/jbr" if env.get("ProgramFiles") else None,
            Path(localapp) / "Programs/Android Studio/jbr" if localapp else None,
            Path(java_exe).resolve().parent.parent if java_exe else None,
        ]
        java = next((home for candidate in java_homes if (home := jdk(candidate))), None)
        if not java:
            stage = state / f"java-download-{uuid4().hex}"
            try:
                fetch_zip(JDK_URL, JDK_SHA, stage)
                java_root.mkdir(exist_ok=True)
                for candidate in stage.iterdir():
                    if jdk(candidate):
                        candidate.replace(java_root / candidate.name)
                        break
            finally:
                rmtree(stage, ignore_errors=True)
            java = next((p for p in java_root.glob("*") if jdk(p)), None)
            if not java:
                raise RuntimeError("Не найдена распакованная Java.")
        env = env | {"JAVA_HOME": str(java)}
    if wants_sdk and not sdk_cli(sdk):
        stage = state.resolve() / f"android-cli-{uuid4().hex}"
        stage.mkdir()
        try:
            fetch_zip(SDK_URL, SDK_SHA, stage)
            copytree(stage / "cmdline-tools", sdk / "cmdline-tools/latest", dirs_exist_ok=True)
        finally:
            rmtree(stage, ignore_errors=True)
    env = project_android_environment(state, sdk, env)
    if wants_sdk:
        flag = f"--sdk_root={sdk}"
        print("Устанавливаю ADB, эмулятор и образ Android 35.", flush=True)
        sdk_java(java, sdk, "sdkmanager", flag, "platform-tools", "emulator", SYSTEM_IMAGE,
                 env=env, choices="y\n" * 30)
        if not sdk_complete(sdk):
            raise RuntimeError("SDK установлен частично. Проверьте сеть и свободное место.")
    if not avd_exists:
        print(f"Создаю виртуальное устройство {AVD}.", flush=True)
        sdk_java(java, sdk, "avdmanager", "create", "avd", "-f", "-n", AVD, "-k", SYSTEM_IMAGE,
                 env=env, choices="no\n")
    return sdk, AVD, env


def emulator_port():
    for port in range(5554, 5683, 2):
        try:
            with socket.socket() as console, socket.socket() as debug:
                for listener, candidate in ((console, port), (debug, port + 1)):
                    listener.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
                    listener.bind(("127.0.0.1", candidate))
                return port
        except OSError:
            continue
    raise RuntimeError("Нет свободной пары портов для эмулятора.")


def adb_run(adb, *args, env, capture=False, check=True, timeout=150):
    return subprocess.run([adb, *args], env=env, check=check, timeout=timeout,
                          capture_output=capture, text=True, errors="replace")


def close_avd(adb, serial, env):
    try:
        adb_run(adb, "-s", serial, "emu", "kill", env=env, capture=True, check=False, timeout=8)
    except (OSError, subprocess.SubprocessError):
        pass


def emulator_option(emulator, option, description, env):
    help_result = subprocess.run([emulator, "-help-" + option], env=env, timeout=15,
                                 capture_output=True, text=True, errors="replace")
    return help_result.returncode == 0 and description in (help_result.stdout + help_result.stderr)


def start_private_adb(stack, state, adb, env):
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]
    env = env | {"ADB_SERVER_SOCKET": f"tcp:127.0.0.1:{port}",
                 "ANDROID_ADB_SERVER_PORT": str(port)}
    server = start(stack, [adb, "-L", f"tcp:{port}", "nodaemon", "server"],
                   state, "adb", env, hidden=True)
    until = monotonic() + 10
    while monotonic() < until and server.poll() is None:
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=.25):
                if server.poll() is None:
                    port_file = state / "adb-port.txt"
                    port_file.write_text(str(port), encoding="ascii")
                    stack.callback(port_file.unlink, missing_ok=True)
                    return env
        except OSError:
            sleep(.1)
    raise RuntimeError("Локальный ADB не запустился. См. .local/adb.err.log.")


def android_start(stack, state, sdk_path, api_port, env):
    if not APK.is_file():
        raise RuntimeError("Не найден учебный APK.")
    print("Подготавливаю Android.", flush=True)
    sdk, name, android_env = provision_android(state, sdk_path, env)
    adb = str(sdk / "platform-tools/adb.exe")
    emulator = str(sdk / "emulator/emulator.exe")
    acceleration = subprocess.run([emulator, "-accel-check"], env=android_env, timeout=30,
                                  capture_output=True, text=True, errors="replace")
    if acceleration.returncode != 0 or not re.search(r"^accel:\s*0\s*$", acceleration.stdout, re.M):
        details = (acceleration.stdout + acceleration.stderr).strip()
        print(f"Аппаратное ускорение Android недоступно.\n{details}", flush=True)
        if confirm("Проверить и включить платформу гипервизора Windows с повышенными правами?"):
            result = system_setup("EnableHypervisor")
            if result == 10:
                raise RestartRequired("Перезагрузите Windows и снова откройте Start-GamingClub.cmd.")
            if result == 11:
                raise RuntimeError("Включите VT-x или SVM (AMD-V) в BIOS/UEFI, затем снова запустите GamingClub.")
            if result != 0:
                raise RuntimeError(f"Настройка гипервизора не выполнена (код {result}).")
        raise RuntimeError("Ускорение Android недоступно. Проверьте поддержку VT-x/AMD-V и политику Windows.")
    android_env = start_private_adb(stack, state, adb, android_env)
    port = emulator_port()
    serial = f"emulator-{port}"
    print(f"Запускаю эмулятор {name}. Первая загрузка может занять несколько минут.", flush=True)
    command = [emulator, "-avd", name, "-port", str(port), "-no-snapshot", "-gpu", "swiftshader_indirect",
               "-camera-back", "emulated", "-camera-front", "none"]
    if emulator_option(emulator, "crash-report-mode", "Available options: disabled", android_env):
        command.extend(["-crash-report-mode", "disabled"])
    if emulator_option(emulator, "no-metrics", "Disable metrics reporting", android_env):
        command.append("-no-metrics")
    process = start(stack, command, state, "emulator", android_env)
    stack.callback(close_avd, adb, serial, android_env)
    until = monotonic() + 480
    while monotonic() < until:
        if process.poll() is not None:
            raise RuntimeError("Эмулятор завершился. См. .local/emulator.err.log.")
        devices = adb_run(adb, "devices", env=android_env, capture=True, check=False).stdout
        if re.search(rf"^{serial}\s+device\b", devices, re.M):
            boot = adb_run(adb, "-s", serial, "shell", "getprop", "sys.boot_completed",
                           env=android_env, capture=True, check=False)
            if boot.stdout.strip() == "1":
                break
        sleep(3)
    else:
        raise RuntimeError("Эмулятор не загрузился за 8 минут.")
    print("Устанавливаю приложение GamingClub в Android.", flush=True)
    adb_run(adb, "-s", serial, "install", "-r", str(APK), env=android_env)
    activity = adb_run(adb, "-s", serial, "shell", "am", "start", "-S", "-W", "-n",
                       "com.gamingclub.client/.MainActivity", "--ei", "api_port", str(api_port),
                       env=android_env, capture=True, check=False)
    if activity.returncode != 0 or not re.search(r"^Status: ok\s*$", activity.stdout, re.M):
        raise RuntimeError("Android-приложение не открылось. " + (activity.stdout + activity.stderr).strip())
    print(f"Приложение GamingClub открыто на {serial}.", flush=True)


def main():
    opts = ArgumentParser(description="Локальный стенд GamingClub для Windows x64")
    opts.add_argument("-NoAndroid", action="store_true")
    opts.add_argument("-ServerOnly", action="store_true")
    opts.add_argument("-AndroidSdk", type=lambda value: Path(value).resolve())
    opts.add_argument("-SmokeSeconds", type=int, default=0, help="Время прогона после старта")
    args = opts.parse_args()
    if os.name != "nt" or platform.machine().lower() not in ("amd64", "x86_64"):
        opts.error("Для этого комплекта требуется Windows x64")
    if not API.is_file():
        opts.error("Не найдена сборка API. Распакуйте ZIP полностью.")
    if not args.ServerOnly and not WPF.is_file():
        opts.error("Не найдена сборка WPF. Распакуйте ZIP полностью.")
    state = ROOT / ".local"
    state.mkdir(exist_ok=True)
    env = os.environ.copy()
    for name in ("ASPNETCORE_URLS", "DOTNET_URLS", "ASPNETCORE_HTTP_PORTS", "ASPNETCORE_HTTPS_PORTS",
                 "GAMINGCLUB_DEMO_ADMIN_PASSWORD", "GAMINGCLUB_DEMO_VISITOR_PASSWORD"):
        env.pop(name, None)
    android_failed = False
    try:
        with launcher_lock(state), ExitStack() as stack:
            own_process_tree()
            server_env, admin_password, visitor_password = provision_database(stack, state, env)
            base, port = active_api(stack, state, server_env)
            print(f"WPF (ADMIN):       club_admin_18   /   {admin_password}", flush=True)
            print(f"Android (VISITOR): club_visitor_18 /   {visitor_password}", flush=True)
            print("Swagger:", base + "/swagger", flush=True)
            if not args.ServerOnly:
                desktop = subprocess.Popen([str(WPF)], cwd=WPF.parent, env=env | {"GAMINGCLUB_API_URL": base})
                stack.callback(stop, desktop)
                if not args.NoAndroid:
                    try:
                        with ExitStack() as android:
                            android_start(android, state, args.AndroidSdk, port, env)
                            stack.enter_context(android.pop_all())
                    except (OSError, RuntimeError, EOFError, subprocess.SubprocessError) as exc:
                        android_failed = True
                        message = str(exc) or "Для первой установки откройте Start-MFC.cmd и подтвердите условия SDK."
                        print(f"Android: {message}\nAPI и WPF доступны.", flush=True)
                        if args.SmokeSeconds:
                            return 1
            if args.SmokeSeconds > 0:
                sleep(args.SmokeSeconds)
            else:
                while input("Для завершения введите STOP: ").strip().upper() != "STOP":
                    pass
                print("Закрываю процессы GamingClub.", flush=True)
    except (KeyboardInterrupt, EOFError):
        print("Стенд остановлен.")
    except (OSError, RuntimeError, subprocess.SubprocessError) as exc:
        print(exc, file=sys.stderr)
        return 1
    return int(android_failed)


if __name__ == "__main__":
    raise SystemExit(main())
