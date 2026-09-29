using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Sotn;

namespace SotnArchipelago;

// Start new games at Castle Entrance instead of the Richter prologue when the seed's
// remove_prologue option is on. Same approach as the recomp's own Skip Prologue
// (RandoPatch.SkipPrologue), which only runs with its built-in randomizer.
static class Prologue
{
    const int PrologueStage = 0x1F;
    const int EntranceFirstVisit = 0x41;

    static bool ShouldSkip => ApClient.OptionInt("remove_prologue") > 0;

    // Runs when a new game is set up on the file select screen, before the first stage loads.
    [PostHook("sel", "func_801ACEC0")]
    static void AfterNewGameSetup(CpuContext c, IMemory m)
    {
        if (!ShouldSkip) return;
        if (m.ReadU32(Game.StageIdAddr) != PrologueStage) return;
        m.WriteU16(Game.StageIdAddr, EntranceFirstVisit);

        // As Rom.py's no_prologue does: clear the time-attack records, which the prologue would have
        // reset. Otherwise records from a save loaded earlier in the session carry over, and those
        // bosses don't appear (and their checks would be sent at once).
        for (uint i = 0; i < TimeAttackRecords; i++) m.WriteU32(Progress.TimeAttackAddr + i * 4, 0);
        Log.Info("new game: skipping the prologue");
    }

    const uint TimeAttackRecords = 28;
}
