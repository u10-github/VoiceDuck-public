using System.Reflection;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VoiceDuck.Core;

namespace VoiceDuck.Extensions.WindowsAudio;

public class WindowsAudioSessionVolumeWriter : IAudioSessionVolumeWriter
{
    public void SetVolume(AudioSessionIdentity identity, float volume)
    {
        var targetPid = identity.ProcessId;

        // Skip if PID is unresolved (0): matching by PID 0 would match all
        // unresolved sessions and could accidentally duck protected sessions.
        if (targetPid == 0)
            return;

        // Skip if identity is not fully resolved: empty RenderDeviceId or
        // SessionInstanceIdentifier would collide with other unresolved sessions.
        if (!identity.IsResolved)
            return;

        var enumerator = new MMDeviceEnumerator();

        try
        {
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
                            var pid = TryGetProcessId(control);
                            if (pid != targetPid)
                                continue;

                            var sessionInstanceId = TryGetSessionInstanceIdentifier(control) ?? string.Empty;
                            var currentIdentity = new AudioSessionIdentity(pid, string.Empty, deviceId, sessionInstanceId);

                            if (!currentIdentity.Equals(identity))
                                continue;

                            var volumeControl = control.SimpleAudioVolume;
                            volumeControl.Volume = Math.Clamp(volume, 0.0f, 1.0f);
                            return; // Exact match found, done
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
        }
        finally
        {
            enumerator.Dispose();
        }
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
