using System.Collections.Specialized;
using System.ComponentModel;

using FlexDir.App.ViewModels;

using Xunit;

namespace FlexDir.App.Tests.ViewModels;

/// <summary>
/// 열거 중 배치 추가에 쓰는 컬렉션. 알림을 <b>배치당 한 번</b>으로 줄이는 것이 존재 이유다 —
/// 항목마다 <c>Add</c> 알림을 내면 256개 배치가 256번의 레이아웃 패스가 된다.
/// <para>
/// 알림은 <c>Reset</c> 이다. 그래서 <b>열거 중에만</b> 쓴다 (그때는 선택이 없다).
/// 감시 갱신 경로는 선택을 유지해야 하므로 개별 알림을 쓴다 — step 7 의 일이다.
/// </para>
/// </summary>
public class BulkObservableCollectionTests
{
    // ── AddRange ──────────────────────────────────────────────────

    [Fact]
    public void AddRange_AppendsInOrder()
    {
        var collection = new BulkObservableCollection<string> { "a" };

        collection.AddRange(["b", "c"]);

        Assert.Equal(["a", "b", "c"], collection);
    }

    [Fact]
    public void AddRange_RaisesOneResetForTheWholeBatch()
    {
        var collection = new BulkObservableCollection<string>();
        var events = Record(collection);

        collection.AddRange(["a", "b", "c"]);

        var single = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, single.Action);
    }

    [Fact]
    public void AddRange_RaisesCountAndIndexerOnce()
    {
        // Count 알림이 없으면 상태표시줄 같은 바인딩이 갱신되지 않는다.
        var collection = new BulkObservableCollection<string>();
        var changed = RecordProperties(collection);

        collection.AddRange(["a", "b"]);

        Assert.Equal(["Count", "Item[]"], changed);
    }

    [Fact]
    public void AddRange_Empty_RaisesNothing()
    {
        var collection = new BulkObservableCollection<string> { "a" };
        var events = Record(collection);
        var changed = RecordProperties(collection);

        collection.AddRange([]);

        Assert.Empty(events);
        Assert.Empty(changed);
        Assert.Equal(["a"], collection);
    }

    [Fact]
    public void AddRange_Null_Throws()
    {
        var collection = new BulkObservableCollection<string>();

        Assert.Throws<ArgumentNullException>(() => collection.AddRange(null!));
    }

    // ── ReplaceAll ────────────────────────────────────────────────

    [Fact]
    public void ReplaceAll_SwapsTheWholeContent()
    {
        var collection = new BulkObservableCollection<string> { "a", "b" };

        collection.ReplaceAll(["c"]);

        Assert.Equal(["c"], collection);
    }

    [Fact]
    public void ReplaceAll_RaisesOneReset()
    {
        var collection = new BulkObservableCollection<string> { "a", "b" };
        var events = Record(collection);

        collection.ReplaceAll(["c", "d", "e"]);

        var single = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, single.Action);
    }

    [Fact]
    public void ReplaceAll_Empty_ClearsAndNotifies()
    {
        // 빈 폴더·오류로 넘어갈 때 이전 폴더 항목이 남으면 잘못된 폴더의 내용으로 보인다.
        var collection = new BulkObservableCollection<string> { "a", "b" };
        var events = Record(collection);

        collection.ReplaceAll([]);

        Assert.Empty(collection);
        Assert.Single(events);
    }

    [Fact]
    public void ReplaceAll_EmptyOnEmpty_RaisesNothing()
    {
        var collection = new BulkObservableCollection<string>();
        var events = Record(collection);

        collection.ReplaceAll([]);

        Assert.Empty(events);
    }

    [Fact]
    public void ReplaceAll_Null_Throws()
    {
        var collection = new BulkObservableCollection<string>();

        Assert.Throws<ArgumentNullException>(() => collection.ReplaceAll(null!));
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────

    private static List<NotifyCollectionChangedEventArgs> Record<T>(BulkObservableCollection<T> collection)
    {
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => events.Add(args);
        return events;
    }

    private static List<string?> RecordProperties<T>(BulkObservableCollection<T> collection)
    {
        var changed = new List<string?>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        return changed;
    }
}
