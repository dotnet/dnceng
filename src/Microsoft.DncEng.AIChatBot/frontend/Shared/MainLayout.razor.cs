// Copyright (c) Microsoft. All rights reserved.

namespace ClientApp.Shared;

public sealed partial class MainLayout
{
    private readonly MudTheme _theme = new();
    private bool _drawerOpen = true;
    private bool _settingsOpen = false;
    private SettingsPanel? _settingsPanel;

    private bool _isDarkTheme
    {
        get => LocalStorage.GetItem<bool>(StorageKeys.PrefersDarkTheme);
        set => LocalStorage.SetItem<bool>(StorageKeys.PrefersDarkTheme, value);
    }

    private bool _isReversed
    {
        get => LocalStorage.GetItem<bool?>(StorageKeys.PrefersReversedConversationSorting) ?? true;
        set => LocalStorage.SetItem<bool>(StorageKeys.PrefersReversedConversationSorting, value);
    }

    private bool _isRightToLeft =>
        Thread.CurrentThread.CurrentUICulture is { TextInfo.IsRightToLeft: true };

    [Inject] public NavigationManager Nav { get; set; } = null!;
    [Inject] public ILocalStorageService LocalStorage { get; set; } = null!;
    [Inject] public IDialogService Dialog { get; set; } = null!;

    private bool SettingsDisabled => new Uri(Nav.Uri).Segments.LastOrDefault() switch
    {
        "ask" or "chat" => false,
        _ => true
    };

    private bool SortDisabled => new Uri(Nav.Uri).Segments.LastOrDefault() switch
    {
        "voicechat" or "chat" => false,
        _ => true
    };

    private void OnMenuClicked() => _drawerOpen = !_drawerOpen;
    private void OnThemeChanged() => _isDarkTheme = !_isDarkTheme;
    private void OnIsReversedChanged() => _isReversed = !_isReversed;
}
