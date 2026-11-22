using System;
using System.Linq;
using System.Threading.Tasks;
using IPBanManager.Core.Abstractions;
using IPBanManager.Core.Views;

namespace IPBanManager.Core.Presenters;

public class LogPresenter : PresenterBase<ILogView>
{
    private const int MaxLines = 500;
    private readonly ILogReader _reader;

    public LogPresenter(ILogView view, IInstallationLocator locator, ILogReader reader, IAppLogger logger)
        : base(view, locator, logger)
    {
        _reader = reader;
        View.RefreshRequested += (s, e) => OnEvent(RefreshAsync);
    }

    public override async Task RefreshAsync()
    {
        if (!Locator.IsValid) return;
        var text = await Task.Run(() => _reader.ReadTail(MaxLines));
        View.LogText = ApplyFilter(text, View.Filter);
    }

    public static string ApplyFilter(string text, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.IsNullOrEmpty(text)) return text;
        var lines = text.Split('\n')
            .Where(l => l.IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(l => l.TrimEnd('\r'));
        return string.Join(Environment.NewLine, lines);
    }
}
