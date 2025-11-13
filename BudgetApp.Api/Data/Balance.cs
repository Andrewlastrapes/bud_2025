using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Api.Data;

[Table("Balances")]
public class Balance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("userId")]
    public int UserId { get; set; }

    [Column("balance")]
    public decimal BalanceAmount { get; set; }

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; }

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }
}