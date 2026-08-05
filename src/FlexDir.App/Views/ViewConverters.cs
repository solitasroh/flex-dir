using System.Globalization;
using System.Windows.Data;

namespace FlexDir.App.Views;

/// <summary>
/// 행이 선택돼 있는가 — <c>[선택 이름 집합, 내 이름]</c> 을 받아 bool 을 낸다.
/// <para>
/// 선택은 <c>PaneSelection</c> 이 이름 집합으로 들고 있고 행 인스턴스에는 없다 (ADR-011).
/// 행마다 <c>IsSelected</c> 를 두고 페인이 밀어 넣으면 선택 변경마다 전체 목록을 훑는다 —
/// <c>MultiBinding</c> 은 가상화가 실현한 행만 다시 계산하므로 비용이 보이는 행 수에
/// 비례한다.
/// </para>
/// </summary>
public sealed class NameMembershipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        // 바인딩이 아직 붙지 않으면 UnsetValue 가 들어온다. 예외를 내면 행 전체가 죽는다.
        if (values is not [IReadOnlyCollection<string> selected, string name])
        {
            return false;
        }

        // 집합은 OrdinalIgnoreCase 로 만들어져 있다 (PaneSelection). Contains 로 충분하다.
        return selected.Contains(name);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("표시 전용이다 — 역방향 동기화가 생기면 ADR-011 위반이다.");
}

/// <summary>
/// 행이 포커스 항목인가 — <c>[FocusedName, 내 이름]</c> 을 받아 bool 을 낸다.
/// 비교는 파일시스템과 같이 대소문자를 가리지 않는다.
/// </summary>
public sealed class NameEqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [string focused, string name])
        {
            return false;
        }

        return StringComparer.OrdinalIgnoreCase.Equals(focused, name);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("표시 전용이다 — 역방향 동기화가 생기면 ADR-011 위반이다.");
}

/// <summary>
/// 값이 매개변수와 같은가. 활성 페인 판정에 쓴다 —
/// <c>ActiveSide</c> 를 <c>ConverterParameter</c> 의 <c>PaneSide</c> 와 비교한다.
/// </summary>
public sealed class EnumEqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("표시 전용이다.");
}
