// NX journal entry point for batch export (global namespace, fully-qualified names only).
public class LaserDxfBatchJournal
{
    public static void Main(string[] args)
    {
        LaserDxf.Nx.BatchCommand.Run();
    }

    public static int GetUnloadOption(string arg)
    {
        return (int)NXOpen.Session.LibraryUnloadOption.Immediately;
    }
}
