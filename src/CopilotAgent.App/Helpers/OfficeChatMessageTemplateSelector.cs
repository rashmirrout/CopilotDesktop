using System.Windows;
using System.Windows.Controls;
using CopilotAgent.Office.Models;

namespace CopilotAgent.App.Helpers;

/// <summary>
/// DataTemplateSelector that picks the appropriate template based on <see cref="OfficeChatRole"/>.
/// </summary>
public sealed class OfficeChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }
    public DataTemplate? ManagerTemplate { get; set; }
    public DataTemplate? AssistantTemplate { get; set; }
    public DataTemplate? SystemTemplate { get; set; }
    public DataTemplate? IterationHeaderTemplate { get; set; }
    public DataTemplate? RestCountdownTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not OfficeChatMessage message)
            return base.SelectTemplate(item, container);

        return message.Role switch
        {
            OfficeChatRole.User => UserTemplate,
            OfficeChatRole.Manager => ManagerTemplate,
            OfficeChatRole.Assistant => AssistantTemplate,
            OfficeChatRole.System => SystemTemplate,
            OfficeChatRole.IterationHeader => IterationHeaderTemplate,
            OfficeChatRole.RestCountdown => RestCountdownTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}