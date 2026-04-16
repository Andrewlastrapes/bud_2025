using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Api.Data;

[Table("PlaidItems")]
public class PlaidItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("institution_name")]
    public string? InstitutionName { get; set; }

    [Column("institution_logo")]
    public string? InstitutionLogo { get; set; }

    [Column("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [Column("itemIds")]
    public string ItemId { get; set; } = string.Empty;

    [Column("userId")]
    public int UserId { get; set; }

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; }

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [Column("cursor")]
    public string? Cursor { get; set; }

    /// <summary>
    /// Set to UTC now the first time a non-backfill sync completes for this item.
    /// Null while the initial Plaid backfill (INITIAL_UPDATE / HISTORICAL_UPDATE) is still
    /// running. Notifications are only sent for transactions whose CreatedAt is on or after
    /// this timestamp, so backfill transactions never trigger push notifications.
    /// </summary>
    [Column("notifications_enabled_at")]
    public DateTime? NotificationsEnabledAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }
}
