#nullable enable
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace SotnArchipelago;

// Three breakable walls set their drop's item id with an instruction immediate
// (`ori vX, zero, id` then `sh vX, 0x30(new entity)`). The AP patch changes that immediate; the
// recomp reads the patched value back from RAM for the NO3/NP3 rocks (RandoPatch.cs, e.g.
// EntityMermanRockLeftSide_no3) but not for these three. Same convention here: remember the entity
// the wall creates, and once the wall function returns, give it the id found at the immediate's RAM
// address. SeedPlan writes the seed's id there; unpatched, it still holds the vanilla id.
static class WallDrops
{
    const uint ItemParamOffset = 0x30;

    static uint _pending;

    static void Remember(CpuContext c, uint callReturn)
    {
        if (c.RA == callReturn) _pending = c.A2;
    }

    static void Apply(IMemory m, uint immediateAddr)
    {
        if (_pending == 0) return;
        m.WriteU16(_pending + ItemParamOffset, m.ReadU16(immediateAddr));
        _pending = 0;
    }

    // Outer Wall, behind the Armor Lord: func_us_801BE880 creates the drop at 0x801BEAA4,
    // then sets its id at 0x801BEAB0.
    [PreHook("no1", "CreateEntityFromEntity_no1")]
    static void No1Create(CpuContext c, IMemory m) => Remember(c, 0x801BEAAC);

    [PostHook("no1", "func_us_801BE880")]
    static void No1Wall(CpuContext c, IMemory m) => Apply(m, 0x801BEAB0);

    // Reverse Outer Wall, below the mist crate: func_801A7B34, create at 0x801A7D58, id at 0x801A7D64.
    [PreHook("rno1", "RNO1_CreateEntityFromEntity")]
    static void Rno1Create(CpuContext c, IMemory m) => Remember(c, 0x801A7D60);

    [PostHook("rno1", "func_801A7B34")]
    static void Rno1Wall(CpuContext c, IMemory m) => Apply(m, 0x801A7D64);

    // Reverse Entrance, big rock: func_801B2BF0, create at 0x801B2EFC, id at 0x801B2F04.
    [PreHook("rno3", "CreateEntityFromEntity_rno3")]
    static void Rno3Create(CpuContext c, IMemory m) => Remember(c, 0x801B2F04);

    [PostHook("rno3", "func_801B2BF0")]
    static void Rno3Wall(CpuContext c, IMemory m) => Apply(m, 0x801B2F04);
}
