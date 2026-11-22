using System;
using IPBanManager.Core.Views;

namespace IPBanManager.Wpf.Views;

public partial class AdvancedView : EditableViewBase, IAdvancedView
{
    public AdvancedView() => InitializeComponent();

    public event EventHandler SaveRequested;
    public event EventHandler ReloadRequested;

    public string Xml
    {
        get => XmlBox.Text;
        set => WithoutDirtyTracking(() => XmlBox.Text = value);
    }

    public override void RequestSave() => SaveRequested?.Invoke(this, EventArgs.Empty);

    public override void RequestReload()
    {
        if (IsDirty && !Confirm("Descartar as alterações não salvas e recarregar do arquivo?")) return;
        ReloadRequested?.Invoke(this, EventArgs.Empty);
    }
}
