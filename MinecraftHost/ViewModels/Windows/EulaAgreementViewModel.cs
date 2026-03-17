using YukkuriMovieMaker.Commons;

namespace MinecraftHost.ViewModels.Windows;

public class EulaAgreementViewModel : Bindable
{
    public ActionCommand AgreeCommand { get; }
    public ActionCommand DeclineCommand { get; }

    public EulaAgreementViewModel(Action agreeAction, Action declineAction)
    {
        AgreeCommand = new ActionCommand(_ => true, _ => agreeAction());
        DeclineCommand = new ActionCommand(_ => true, _ => declineAction());
    }
}