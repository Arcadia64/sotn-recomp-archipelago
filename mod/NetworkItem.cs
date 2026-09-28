namespace SotnArchipelago;

// An item as the Archipelago server describes it (scouts, received items).
public readonly record struct NetworkItem(long Item, long Location, int Player, int Flags)
{
    public bool Progression => (Flags & 0b001) != 0;
    public bool Useful => (Flags & 0b010) != 0;
    public bool Trap => (Flags & 0b100) != 0;
}
