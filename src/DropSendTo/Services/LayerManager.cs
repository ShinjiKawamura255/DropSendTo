namespace DropSendTo.Services;

public class LayerManager
{
    public int Current { get; private set; }
    public LayerManager(int initial = 0)
    {
        Set(initial);
    }

    public void Set(int index)
    {
        if (index < 0) index = 0;
        if (index > 3) index = 3;
        Current = index;
    }

    public void Next() => Current = (Current + 1) % 4;
    public void Prev() => Current = (Current + 3) % 4;
}

