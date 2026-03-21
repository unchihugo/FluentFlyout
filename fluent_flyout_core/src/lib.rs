use std::ffi::{c_char, CString};
use std::sync::OnceLock;
use windows::Media::Control::*;

static MANAGER: OnceLock<GlobalSystemMediaTransportControlsSessionManager> = OnceLock::new();

fn get_manager() -> Option<&'static GlobalSystemMediaTransportControlsSessionManager> {
    MANAGER.get_or_init(|| {
        GlobalSystemMediaTransportControlsSessionManager::RequestAsync()
            .unwrap()
            .get()
            .unwrap()
    });
    MANAGER.get()
}

fn get_media_session(exclusive: bool) -> Option<GlobalSystemMediaTransportControlsSession> {
    let manager = get_manager()?;
    if exclusive {
        let sessions = manager.GetSessions().ok()?;
        for session in sessions {
            if let Ok(id) = session.SourceAppUserModelId() {
                let id_str = id.to_string().to_uppercase();
                if id_str.contains("TIDAL") {
                    return Some(session);
                }
            }
        }
        None
    } else {
        manager.GetCurrentSession().ok()
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn get_media_title(exclusive: bool) -> *mut c_char {
    let Some(session) = get_media_session(exclusive) else { return std::ptr::null_mut() };
    let Ok(info) = session.TryGetMediaPropertiesAsync().and_then(|op| op.get()) else { return std::ptr::null_mut() };
    let Ok(title) = info.Title() else { return std::ptr::null_mut() };
    
    CString::new(title.to_string()).map(|c| c.into_raw()).unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn get_media_artist(exclusive: bool) -> *mut c_char {
    let Some(session) = get_media_session(exclusive) else { return std::ptr::null_mut() };
    let Ok(info) = session.TryGetMediaPropertiesAsync().and_then(|op| op.get()) else { return std::ptr::null_mut() };
    let Ok(artist) = info.Artist() else { return std::ptr::null_mut() };
    
    CString::new(artist.to_string()).map(|c| c.into_raw()).unwrap_or(std::ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn free_string(s: *mut c_char) {
    if !s.is_null() {
        unsafe {
            let _ = CString::from_raw(s);
        }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn media_play_pause(exclusive: bool) -> bool {
    let Some(session) = get_media_session(exclusive) else { return false };
    session.TryTogglePlayPauseAsync().and_then(|op| op.get()).is_ok()
}

#[unsafe(no_mangle)]
pub extern "C" fn media_next(exclusive: bool) -> bool {
    let Some(session) = get_media_session(exclusive) else { return false };
    session.TrySkipNextAsync().and_then(|op| op.get()).is_ok()
}

#[unsafe(no_mangle)]
pub extern "C" fn media_previous(exclusive: bool) -> bool {
    let Some(session) = get_media_session(exclusive) else { return false };
    session.TrySkipPreviousAsync().and_then(|op| op.get()).is_ok()
}
