// SB_Inspect.cs - launcher journal for StandardBlocks.cs, "inspect" command.
// Keep this file in the same folder as StandardBlocks.cs. It only calls into
// StandardBlocks.cs, so the real code lives in one place and ribbon buttons
// can point at small launchers like this one.

using System;
using System.IO;
using NXOpen;

public class SbInspectLauncher
{
    public static void Main(string[] args)
    {
        Session session = Session.GetSession();
        string main = null;
        try
        {
            main = Path.Combine(Path.GetDirectoryName(session.ExecutingJournal), "StandardBlocks.cs");
            session.Execute(main, "StandardBlocksJournal", "Run", new object[] { "inspect" });
        }
        catch (Exception ex)
        {
            UI.GetUI().NXMessageBox.Show("Standard Blocks", NXMessageBox.DialogType.Error,
                "Could not run " + (main ?? "StandardBlocks.cs") + ":\n" + ex.Message);
        }
    }

    public static int GetUnloadOption(string dummy)
    {
        return (int)Session.LibraryUnloadOption.Immediately;
    }
}
