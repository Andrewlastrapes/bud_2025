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

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }
}