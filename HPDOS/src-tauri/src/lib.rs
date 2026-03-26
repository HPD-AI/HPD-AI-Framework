use std::{
    env, fs,
    path::PathBuf,
    sync::{Arc, Mutex},
    thread,
    time::{Duration, Instant},
};

use tauri::{Manager, RunEvent, WebviewUrl, WebviewWindowBuilder};

type BackendLib = Arc<Mutex<Option<libloading::Library>>>;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let lib: BackendLib = Arc::new(Mutex::new(None));
    let lib_for_exit = lib.clone();

    tauri::Builder::default()
        .manage(lib)
        .setup(|app| {
            let win = WebviewWindowBuilder::new(
                app,
                "main",
                WebviewUrl::External("about:blank".parse().unwrap()),
            )
            .title("HPDOS")
            .inner_size(1280.0, 800.0)
            .resizable(true)
            .visible(false)
            .devtools(true)
            .build()?;

            let handle = app.handle().clone();
            thread::spawn(move || match launch_backend(&handle) {
                Ok(url_str) => {
                    let url: url::Url = url_str.parse().expect("invalid backend URL");
                    if let Err(e) = win.navigate(url) {
                        eprintln!("[hpdos-app] navigate failed: {e}");
                        handle.exit(1);
                        return;
                    }
                    let _ = win.show();
                }
                Err(e) => {
                    eprintln!("[hpdos-app] Failed to launch backend: {e}");
                    handle.exit(1);
                }
            });
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building tauri application")
        .run(move |_, event| {
            if let RunEvent::Exit = event {
                stop_backend(&lib_for_exit);
            }
        });
}

/// Load the HPDOS.Core dylib and call `hpdos_start`, then poll the port file.
fn launch_backend(app: &tauri::AppHandle) -> Result<String, String> {
    let dylib = hpdos_core_dylib();

    // Tell the dylib where it lives so it can find wwwroot and appsettings.json.
    if let Some(dir) = dylib.parent() {
        env::set_var("HPDOS_BASE_DIR", dir);
    }

    // Safety: we own the library for the lifetime of the app.
    let lib = unsafe {
        libloading::Library::new(&dylib)
            .map_err(|e| format!("failed to load '{}': {e}", dylib.display()))?
    };

    unsafe {
        let start: libloading::Symbol<unsafe extern "C" fn()> = lib
            .get(b"hpdos_start\0")
            .map_err(|e| format!("symbol hpdos_start not found: {e}"))?;
        start();
    }

    *app.state::<BackendLib>().lock().unwrap() = Some(lib);

    let port = poll_port_file(Duration::from_secs(30))?;
    Ok(format!("http://127.0.0.1:{port}"))
}

fn stop_backend(holder: &BackendLib) {
    if let Ok(mut guard) = holder.lock() {
        if let Some(lib) = guard.as_ref() {
            unsafe {
                if let Ok(stop) =
                    lib.get::<unsafe extern "C" fn()>(b"hpdos_stop\0")
                {
                    stop();
                }
            }
        }
        // Drop the library after stopping — this unloads the dylib.
        *guard = None;
    }
}

/// Poll the port file written by `GUIMode.StartServerAsync` until it contains a valid u16,
/// or the timeout expires.
fn poll_port_file(timeout: Duration) -> Result<u16, String> {
    let path = port_file();
    let deadline = Instant::now() + timeout;
    loop {
        thread::sleep(Duration::from_millis(100));
        if let Ok(content) = fs::read_to_string(&path) {
            if let Ok(port) = content.trim().parse::<u16>() {
                return Ok(port);
            }
        }
        if Instant::now() >= deadline {
            return Err(format!(
                "backend did not write port file at '{}' within {:.1}s",
                path.display(),
                timeout.as_secs_f32()
            ));
        }
    }
}

/// Resolve the HPDOS.Core dylib.
/// In a bundled `.app`, it lives alongside the Tauri binary in `Contents/MacOS/`.
/// Falls back to the directory of the current executable for dev builds.
fn hpdos_core_dylib() -> PathBuf {
    let dylib_name = if cfg!(target_os = "macos") {
        "HPDOS.Core.dylib"
    } else if cfg!(target_os = "windows") {
        "HPDOS.Core.dll"
    } else {
        "HPDOS.Core.so"
    };

    // Bundled release: dylib lives next to the Tauri binary in Contents/MacOS/.
    if let Ok(exe) = env::current_exe() {
        if let Some(dir) = exe.parent() {
            let candidate = dir.join(dylib_name);
            if candidate.exists() {
                return candidate;
            }
        }
    }

    // Dev: dylib is in the dotnet publish output folder.
    let dev_candidate = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../src-dotnet/HPDOS.Core/bin/Release/net10.0/osx-arm64/publish")
        .join(dylib_name);
    if dev_candidate.exists() {
        return dev_candidate;
    }

    PathBuf::from(dylib_name)
}

/// Mirrors `HpdosDataPaths.ActivePortFile` in HPDOS.Core.
fn port_file() -> PathBuf {
    #[cfg(target_os = "macos")]
    {
        let home = env::var("HOME").unwrap_or_else(|_| "/tmp".into());
        PathBuf::from(home)
            .join("Library")
            .join("Application Support")
            .join("hpdos")
            .join("port")
    }
    #[cfg(target_os = "linux")]
    {
        let home = env::var("HOME").unwrap_or_else(|_| "/tmp".into());
        PathBuf::from(home).join(".config").join("hpdos").join("port")
    }
    #[cfg(target_os = "windows")]
    {
        let appdata =
            env::var("APPDATA").unwrap_or_else(|_| "C:\\Users\\Public\\AppData\\Roaming".into());
        PathBuf::from(appdata).join("hpdos").join("port")
    }
}
