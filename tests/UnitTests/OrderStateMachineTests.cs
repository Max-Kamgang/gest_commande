using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.OrderService.Domain;
using Xunit;

namespace GestionCommandes.UnitTests;

/// <summary>Machine a etats de la commande : seules les transitions du cahier des charges sont permises.</summary>
public class OrderStateMachineTests
{
    [Theory]
    [InlineData(OrderStatus.Brouillon, OrderStatus.Confirmee)]
    [InlineData(OrderStatus.Brouillon, OrderStatus.Annulee)]
    [InlineData(OrderStatus.Confirmee, OrderStatus.EnPreparation)]
    [InlineData(OrderStatus.Confirmee, OrderStatus.Annulee)]
    [InlineData(OrderStatus.EnPreparation, OrderStatus.Expediee)]
    [InlineData(OrderStatus.EnPreparation, OrderStatus.Annulee)]
    [InlineData(OrderStatus.Expediee, OrderStatus.Livree)]
    public void Transitions_autorisees_sont_acceptees(OrderStatus from, OrderStatus to)
    {
        Assert.True(OrderStateMachine.CanTransition(from, to));
        OrderStateMachine.EnsureCanTransition(from, to, "CMD-TEST");
    }

    [Theory]
    [InlineData(OrderStatus.Brouillon, OrderStatus.Expediee)]
    [InlineData(OrderStatus.Brouillon, OrderStatus.Livree)]
    [InlineData(OrderStatus.Confirmee, OrderStatus.Livree)]
    [InlineData(OrderStatus.Expediee, OrderStatus.Annulee)]
    [InlineData(OrderStatus.Expediee, OrderStatus.EnPreparation)]
    [InlineData(OrderStatus.Livree, OrderStatus.Annulee)]
    [InlineData(OrderStatus.Annulee, OrderStatus.Confirmee)]
    [InlineData(OrderStatus.Confirmee, OrderStatus.Confirmee)]
    public void Transitions_interdites_renvoient_un_conflit_explicite(OrderStatus from, OrderStatus to)
    {
        Assert.False(OrderStateMachine.CanTransition(from, to));

        var exception = Assert.Throws<ConflictException>(() =>
            OrderStateMachine.EnsureCanTransition(from, to, "CMD-TEST"));

        Assert.Equal("INVALID_STATE_TRANSITION", exception.ErrorCode);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, exception.StatusCode);
    }

    [Theory]
    [InlineData(OrderStatus.Livree)]
    [InlineData(OrderStatus.Annulee)]
    public void Les_etats_finaux_n_ont_aucune_transition(OrderStatus etat)
    {
        Assert.True(OrderStateMachine.IsFinal(etat));
        Assert.Empty(OrderStateMachine.NextStates(etat));
    }

    [Fact]
    public void Une_annulation_est_possible_jusqua_lexpedition_seulement()
    {
        Assert.Contains(OrderStatus.Brouillon, OrderStateMachine.CancellableStates);
        Assert.Contains(OrderStatus.Confirmee, OrderStateMachine.CancellableStates);
        Assert.Contains(OrderStatus.EnPreparation, OrderStateMachine.CancellableStates);
        Assert.DoesNotContain(OrderStatus.Expediee, OrderStateMachine.CancellableStates);
    }

    [Fact]
    public void Seules_les_commandes_confirmees_ou_en_preparation_immobilisent_du_stock()
    {
        Assert.True(OrderStateMachine.HoldsStock(OrderStatus.Confirmee));
        Assert.True(OrderStateMachine.HoldsStock(OrderStatus.EnPreparation));
        Assert.False(OrderStateMachine.HoldsStock(OrderStatus.Brouillon));
        Assert.False(OrderStateMachine.HoldsStock(OrderStatus.Expediee));
    }

    [Fact]
    public void Les_lignes_ne_sont_modifiables_qu_en_brouillon()
    {
        Assert.True(OrderStateMachine.AllowsLineEdition(OrderStatus.Brouillon));
        Assert.False(OrderStateMachine.AllowsLineEdition(OrderStatus.Confirmee));
    }
}
