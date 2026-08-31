using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class AuditPage(DbValues<AuditMessage> messages) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
        => EditableView("Audit messages", "View");

    public override async Task<object?> GetModelAsync(Admin admin)
        => await messages.ToCollectionAsync();
}