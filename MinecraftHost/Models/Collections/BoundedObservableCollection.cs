using System.Collections.ObjectModel;

namespace MinecraftHost.Models.Collections;

public class BoundedObservableCollection<T>(int capacity) : ObservableCollection<T>
{
    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    protected override void InsertItem(int index, T item)
    {
        if (Count >= Capacity)
            RemoveAt(0);

        base.InsertItem(index, item);
    }
}