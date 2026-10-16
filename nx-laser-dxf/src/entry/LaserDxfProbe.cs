// NX journal entry point (global namespace, fully-qualified names only, so the
// NXOpen namespace never leaks into the shared pure-logic code).
public class LaserDxfProbeJournal
{
    public static void Main(string[] args)
    {
        LaserDxf.Nx.ProbeCommand.Run();
    }

    public static int GetUnloadOption(string arg)
    {
        return (int)NXOpen.Session.LibraryUnloadOption.Immediately;
    }
}
