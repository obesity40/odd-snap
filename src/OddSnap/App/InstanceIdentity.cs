namespace OddSnap;

/// <summary>
/// Identifies this process instance for single-instance coordination
/// (mutex + activation pipe).
/// </summary>
/// <remarks>
/// Debug builds use a distinct suffix so a locally built binary can run at the
/// same time as an installed release build. Without this, the debug build
/// acquires nothing (the release build already holds the mutex), forwards an
/// activation request and exits — making it look like "my changes have no
/// effect" because the installed copy is brought to the foreground instead.
/// </remarks>
internal static class InstanceIdentity
{
#if DEBUG
    private const string Suffix = "Debug";
#else
    private const string Suffix = "";
#endif

    /// <summary>Base name shared by the mutex and the activation pipe.</summary>
    public const string BaseName = "OddSnapScreenshotTool";

    /// <summary>Mutex name guarding the single-instance check.</summary>
    public static string MutexName => $"{BaseName}_SingleInstance{Suffix}";

    /// <summary>
    /// Prefix for the activation pipe. The owning user's SID is appended by
    /// the caller to keep instances separated per Windows account.
    /// </summary>
    public static string ActivationPipePrefix => $"{BaseName}_Activation{Suffix}_";
}
