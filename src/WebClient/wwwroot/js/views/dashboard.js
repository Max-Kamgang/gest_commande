/** Tableau de bord : indicateurs consolides de la plateforme. */

import { api, estBackOffice, aRole } from '../api.js';
import { chargement, indicateur, tableau, etiquette, montant, nombre, dateHeure, erreur, texte } from '../ui.js';

export const titre = 'Tableau de bord';
export const sousTitre = 'Vue consolidee des commandes, du stock et des livraisons';

export async function afficher(cible) {
    chargement(cible);

    try {
        // Les appels partent en parallele : une page, un aller-retour perceptible.
        const [commandes, stockBas, livraisons, notifications] = await Promise.all([
            estBackOffice() || aRole('Client')
                ? api.get('/api/orders', { pageSize: 5, sort: 'createdat_desc' })
                : Promise.resolve(null),
            estBackOffice()
                ? api.get('/api/stock/low-stock', { pageSize: 5 })
                : Promise.resolve(null),
            estBackOffice() || aRole('Livreur')
                ? api.get('/api/deliveries', { pageSize: 5 })
                : Promise.resolve(null),
            api.get('/api/notifications/summary').catch(() => null)
        ]);

        const parEtat = estBackOffice() ? await compterParEtat() : null;

        cible.innerHTML = `
            <div class="grille-indicateurs">
                ${commandes ? indicateur('Commandes', nombre(commandes.totalCount), 'toutes periodes') : ''}
                ${parEtat ? indicateur('A preparer', nombre(parEtat.Confirmee), 'commandes confirmees') : ''}
                ${parEtat ? indicateur('En preparation', nombre(parEtat.EnPreparation), 'a expedier') : ''}
                ${livraisons ? indicateur('Livraisons', nombre(livraisons.totalCount), 'bordereaux') : ''}
                ${stockBas ? indicateur('Alertes stock', nombre(stockBas.totalCount), 'sous le seuil') : ''}
                ${notifications ? indicateur('Notifications', nombre(notifications.total), `${nombre(notifications.unread)} non lue(s)`) : ''}
            </div>

            <div class="grille-deux">
                ${commandes ? carteCommandes(commandes) : ''}
                ${stockBas ? carteStockBas(stockBas) : ''}
                ${livraisons ? carteLivraisons(livraisons) : ''}
                ${notifications ? carteNotifications(notifications) : ''}
            </div>`;
    } catch (exception) {
        erreur(exception, 'Chargement du tableau de bord impossible');
        cible.innerHTML = `<div class="carte"><div class="vide">Les donnees n'ont pas pu etre chargees. Utilisez « Rafraichir ».</div></div>`;
    }
}

/** Compte les commandes par etat : un appel leger par etat (pageSize=1). */
async function compterParEtat() {
    const etats = ['Brouillon', 'Confirmee', 'EnPreparation', 'Expediee', 'Livree', 'Annulee'];

    const resultats = await Promise.all(
        etats.map((etat) =>
            api.get('/api/orders', { status: etat, pageSize: 1 })
                .then((page) => [etat, page.totalCount])
                .catch(() => [etat, 0]))
    );

    return Object.fromEntries(resultats);
}

function carteCommandes(page) {
    return `
        <div class="carte">
            <h3>Dernieres commandes</h3>
            <p class="sous-titre">Les cinq commandes les plus recentes</p>
            ${tableau([
                { titre: 'Numero', rendu: (o) => `<code>${texte(o.number)}</code>` },
                { titre: 'Client', rendu: (o) => texte(o.customerName || '-') },
                { titre: 'Etat', rendu: (o) => etiquette(o.status) },
                { titre: 'Total', numerique: true, rendu: (o) => montant(o.total, o.currency) }
            ], page.items, 'Aucune commande enregistree.')}
        </div>`;
}

function carteStockBas(page) {
    return `
        <div class="carte">
            <h3>Alertes de stock</h3>
            <p class="sous-titre">Produits au niveau du seuil d'alerte ou en dessous</p>
            ${tableau([
                { titre: 'Produit', rendu: (s) => texte(s.productLabel || s.productId.slice(0, 8)) },
                { titre: 'Entrepot', rendu: (s) => texte(s.warehouseCode) },
                { titre: 'Dispo', numerique: true, rendu: (s) => nombre(s.quantityAvailable) },
                { titre: 'Seuil', numerique: true, rendu: (s) => nombre(s.lowStockThreshold) }
            ], page.items, 'Aucune alerte : tous les stocks sont au-dessus du seuil.')}
        </div>`;
}

function carteLivraisons(page) {
    return `
        <div class="carte">
            <h3>Livraisons recentes</h3>
            <p class="sous-titre">Bordereaux et etat d'avancement</p>
            ${tableau([
                { titre: 'Reference', rendu: (d) => `<code>${texte(d.reference)}</code>` },
                { titre: 'Commande', rendu: (d) => texte(d.orderNumber) },
                { titre: 'Livreur', rendu: (d) => texte(d.courierName || 'non affecte') },
                { titre: 'Etat', rendu: (d) => etiquette(d.status) }
            ], page.items, 'Aucune livraison enregistree.')}
        </div>`;
}

function carteNotifications(resume) {
    const parType = Object.entries(resume.byEventType || {})
        .map(([type, total]) => ({ type, total }));

    return `
        <div class="carte">
            <h3>Notifications</h3>
            <p class="sous-titre">
                ${nombre(resume.critical)} critique(s), ${nombre(resume.warning)} avertissement(s)
                &middot; derniere : ${dateHeure(resume.lastReceivedAt)}
            </p>
            ${tableau([
                { titre: 'Type d\'evenement', rendu: (l) => `<code>${texte(l.type)}</code>` },
                { titre: 'Nombre', numerique: true, rendu: (l) => nombre(l.total) }
            ], parType, 'Aucun evenement recu pour le moment.')}
        </div>`;
}
