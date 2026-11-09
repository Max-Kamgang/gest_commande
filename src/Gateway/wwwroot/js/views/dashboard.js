/** Tableau de bord : compteurs de commandes, alertes de stock, derniers mouvements. */

import { api, estBackOffice } from '../api.js';
import { chargement, dateCourte, erreur, etiquette, indicateur, montant, nombre, tableau, texte } from '../ui.js';

export const titre = 'Tableau de bord';
export const sousTitre = 'Vue d\'ensemble des commandes, du stock et des livraisons';

const ETATS_SUIVIS = ['Brouillon', 'Confirmee', 'EnPreparation', 'Expediee', 'Livree'];

export async function afficher(cible) {
    chargement(cible, 'Chargement du tableau de bord...');

    try {
        // Un appel par compteur, tous en parallele : une seule attente a l'ecran.
        const [compteurs, commandes, livraisons, stockBas] = await Promise.all([
            Promise.all(ETATS_SUIVIS.map((etat) =>
                api.get('/api/orders', { status: etat, pageSize: 1 })
                    .then((page) => ({ etat, total: page.totalCount }))
                    .catch(() => ({ etat, total: 0 })))),
            api.get('/api/orders', { pageSize: 5 }).catch(() => ({ items: [] })),
            api.get('/api/deliveries', { pageSize: 5 }).catch(() => ({ items: [] })),
            estBackOffice() ? api.get('/api/stock/low-stock').catch(() => []) : Promise.resolve([])
        ]);

        cible.innerHTML = `
            <div class="indicateurs">
                ${compteurs.map((compteur) =>
                    indicateur(libelleEtat(compteur.etat), nombre(compteur.total))).join('')}
                ${estBackOffice()
                    ? indicateur('Alertes de stock', nombre(stockBas.length), 'produits sous leur seuil')
                    : ''}
            </div>

            <section class="bloc">
                <h3>Dernieres commandes</h3>
                ${tableau([
                    { titre: 'Numero', rendu: (c) => `<code>${texte(c.number)}</code>` },
                    { titre: 'Client', rendu: (c) => texte(c.customerName) },
                    { titre: 'Etat', rendu: (c) => etiquette(c.status) },
                    { titre: 'Total', numerique: true, rendu: (c) => montant(c.total, c.currency) },
                    { titre: 'Creee le', rendu: (c) => dateCourte(c.createdAt) }
                ], commandes.items, 'Aucune commande enregistree.')}
            </section>

            <section class="bloc">
                <h3>Dernieres livraisons</h3>
                ${tableau([
                    { titre: 'Bordereau', rendu: (l) => `<code>${texte(l.reference)}</code>` },
                    { titre: 'Commande', rendu: (l) => texte(l.orderNumber) },
                    { titre: 'Client', rendu: (l) => texte(l.customerName) },
                    { titre: 'Etat', rendu: (l) => etiquette(l.status) },
                    { titre: 'Livreur', rendu: (l) => texte(l.courierName || 'non affecte') }
                ], livraisons.items, 'Aucune livraison enregistree.')}
            </section>

            ${estBackOffice() ? blocStockBas(stockBas) : ''}`;
    } catch (exception) {
        erreur(exception, 'Tableau de bord indisponible');
        cible.innerHTML = '<div class="vide">Impossible de charger le tableau de bord.</div>';
    }
}

function blocStockBas(produits) {
    return `
        <section class="bloc">
            <h3>Produits a reapprovisionner</h3>
            ${tableau([
                { titre: 'Reference', rendu: (p) => `<code>${texte(p.sku)}</code>` },
                { titre: 'Produit', rendu: (p) => texte(p.name) },
                { titre: 'Disponible', numerique: true, rendu: (p) => nombre(p.quantityAvailable) },
                { titre: 'Seuil', numerique: true, rendu: (p) => nombre(p.lowStockThreshold) },
                { titre: 'Reserve', numerique: true, rendu: (p) => nombre(p.quantityReserved) }
            ], produits, 'Aucune alerte : tous les stocks sont au-dessus de leur seuil.')}
        </section>`;
}

function libelleEtat(etat) {
    const libelles = {
        Brouillon: 'Brouillons',
        Confirmee: 'Confirmees',
        EnPreparation: 'En preparation',
        Expediee: 'Expediees',
        Livree: 'Livrees'
    };

    return libelles[etat] || etat;
}
