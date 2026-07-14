using System.Reflection;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VoiceDuck.Core;

namespace VoiceDuck.Extensions.WindowsAudio;

public class WindowsAudioSessionVolumeWriter : IAudioSessionVolumeWriter
{
    public VolumeWriteResult SetVolume(AudioSessionIdentity identity, float volume)
    {
        var targetPid = identity.ProcessId;

        if (targetPid == 0)
            return VolumeWriteResult.SessionNotFound;

        if (!identity.IsResolved)
            return VolumeWriteResult.SessionNotFound;

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
                            return VolumeWriteResult.Succeeded;
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
        catch
        {
            return VolumeWriteResult.Failed;
        }
        finally
        {
            enumerator.Dispose();
        }

        return VolumeWriteResult.SessionNotFound;
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
