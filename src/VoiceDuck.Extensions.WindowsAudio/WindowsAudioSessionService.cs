using System.Reflection;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VoiceDuck.Core;

namespace VoiceDuck.Extensions.WindowsAudio;

public class WindowsAudioSessionService : IAudioSessionService
{
    public IReadOnlyList<AudioSessionInfo> GetAllSessions()
    {
        var sessions = new List<AudioSessionInfo>();
        var enumerator = new MMDeviceEnumerator();

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            var deviceId = device.ID;
            try
            {
                var manager = device.AudioSessionManager;
                var collection = manager.Sessions;

                for (var i = 0; i < collection.Count; i++)
                {
                    var control = collection[i];
                    try
                    {
                        var volume = control.SimpleAudioVolume;
                        var displayName = control.DisplayName;
                        var processId = TryGetProcessId(control);
                        var processName = TryGetProcessName(processId) ?? displayName;
                        var sessionInstanceId = TryGetSessionInstanceIdentifier(control) ?? string.Empty;
                        var executablePath = TryGetExecutablePath(processId);

                        var identity = new AudioSessionIdentity(processId, processName, deviceId, sessionInstanceId);
                        sessions.Add(new AudioSessionInfo(identity, volume.Volume, volume.Mute, executablePath));
                    }
                    finally
                    {
                        control.Dispose();
                    }
                }
            }
            finally
            {
                device.Dispose();
            }
        }

        enumerator.Dispose();
        return sessions;
    }

    private static uint TryGetProcessId(AudioSessionControl control)
    {
        try
        {
            var field = typeof(AudioSessionControl).GetField(
                "audioSessionControlInterface",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (field?.GetValue(control) is IAudioSessionControl2 session2)
            {
                session2.GetProcessId(out var pid);
                return pid;
            }
        }
        catch
        {
        }

        return 0;
    }

    private static string? TryGetProcessName(uint processId)
    {
        if (processId == 0) return null;
        try
        {
            var name = System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name += ".exe";
            return name;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetExecutablePath(uint processId)
    {
        if (processId == 0) return null;
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)processId);
            return proc.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetSessionInstanceIdentifier(AudioSessionControl control)
    {
        try
        {
            var field = typeof(AudioSessionControl).GetField(
                "audioSessionControlInterface",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (field?.GetValue(control) is IAudioSessionControl2 session2)
            {
                session2.GetSessionInstanceIdentifier(out var sid);
                return sid;
            }
        }
        catch
        {
        }

        return null;
    }
}
