using System.Reflection;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VoiceDuck.Core;

namespace VoiceDuck.Extensions.WindowsAudio;

public class WindowsMicrophoneStateService : IMicrophoneStateService
{
    public IReadOnlySet<string> GetActiveMicProcessNames()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enumerator = new MMDeviceEnumerator();

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            try
            {
                var manager = device.AudioSessionManager;
                var collection = manager.Sessions;

                for (var i = 0; i < collection.Count; i++)
                {
                    var control = collection[i];
                    try
                    {
                        if (control.State != AudioSessionState.AudioSessionStateActive)
                            continue;

                        var processId = TryGetProcessId(control);
                        var processName = TryGetProcessName(processId);
                        if (processName != null)
                            result.Add(processName);
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
        return result;
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
}
