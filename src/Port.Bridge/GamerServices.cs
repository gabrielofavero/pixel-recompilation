using System.Collections;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Storage;

namespace Microsoft.Xna.Framework.GamerServices;

public static class Guide
{
    public static bool IsVisible => false;
    public static bool IsTrialMode => false;
    public static IAsyncResult BeginShowStorageDeviceSelector(AsyncCallback callback, object state)
    {
        var result = new CompletedResult(state);
        callback?.Invoke(result);
        return result;
    }
    public static StorageDevice EndShowStorageDeviceSelector(IAsyncResult result)
    {
        if (result is not CompletedResult) throw new ArgumentException("Unknown storage selection result.", nameof(result));
        return new StorageDevice();
    }
    public static void ShowMarketplace(PlayerIndex player) { }
    public static void ShowSignIn(int paneCount, bool onlineOnly) { }

    private sealed class CompletedResult(object state) : IAsyncResult
    {
        public object AsyncState => state;
        public WaitHandle AsyncWaitHandle { get; } = new ManualResetEvent(true);
        public bool CompletedSynchronously => true;
        public bool IsCompleted => true;
    }
}

public class Gamer
{
    public string Gamertag => "Player";
    public static SignedInGamerCollection SignedInGamers { get; } = new();
}

public sealed class SignedInGamer : Gamer
{
    public PlayerIndex PlayerIndex { get; internal set; }
}

public class GamerCollection<T> : IEnumerable<T>
{
    private readonly List<T> items = [];
    protected void Add(T item) => items.Add(item);
    public GamerCollectionEnumerator GetEnumerator() => new(items.GetEnumerator());
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct GamerCollectionEnumerator(IEnumerator<T> source) : IEnumerator<T>
    {
        public T Current => source.Current;
        object IEnumerator.Current => Current!;
        public bool MoveNext() => source.MoveNext();
        public void Reset() => source.Reset();
        public void Dispose() => source.Dispose();
    }
}

public sealed class SignedInGamerCollection : GamerCollection<SignedInGamer>
{
    public SignedInGamerCollection()
    {
        for (var index = 0; index < 4; index++)
            Add(new SignedInGamer { PlayerIndex = (PlayerIndex)index });
    }
}

public sealed class GamerServicesComponent(Game game) : GameComponent(game);
