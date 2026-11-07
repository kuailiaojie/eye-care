using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace EyeCare.Helpers;

/// <summary>
/// 安全解析 WinUI 主题资源：
/// 资源缺失或当前线程没有 XAML 上下文时返回 false / 回退色，而不是抛出 KeyNotFoundException 导致页面崩溃。
/// 注意：部分资源位于 ThemeDictionaries 中，TryGetValue 可能取不到，因此失败后再回退到索引器。
/// </summary>
internal static class ThemeResources
{
    /// <summary>尝试获取应用级资源。</summary>
    public static bool TryGet(string key, out object? value)
    {
        value = null;
        try
        {
            var resources = Application.Current?.Resources;
            if (resources is null) return false;

            if (resources.TryGetValue(key, out var found) && found is not null)
            {
                value = found;
                return true;
            }

            value = resources[key];
            return value is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>解析按钮样式；资源缺失时返回 null（保持控件原有样式）。</summary>
    public static Style? ResolveStyle(string key)
        => TryGet(key, out var value) ? value as Style : null;

    /// <summary>解析画刷；资源缺失时回退到指定颜色。</summary>
    public static Brush ResolveBrush(string key, byte r, byte g, byte b)
        => TryGet(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
}
