/** Stock : niveaux par produit, mouvements, entrees et ajustements d'inventaire. */

import { api } from '../api.js';
import {
    chargement, dateHeure, erreur, etiquette, modale, nombre,
    pagination, succes, tableau, texte, valeursFormulaire
} from '../ui.js';

export const titre = 'Stock';
export const sousTitre = 'Niveaux, mouvements et alertes de reapprovisionnement';

const etat = { page: 1, onglet: 'niveaux', recherche: '' };
let produitsActifs = [];

export async function afficher(cible) {
    chargement(cible, 'Chargement du stock...');

    try {
        cible.innerHTML = `
            <div class="barre-outils">
                <div class="onglets">
                    <button type="button" data-onglet="niveaux" class="${classeOnglet('niveaux')}">Niveaux</button>
                    <button type="button" data-onglet="alertes" class="${classeOnglet('alertes')}">Alertes</button>
                    <button type="button" data-onglet="mouvements" class="${classeOnglet('mouvements')}">Mouvements</button>
                </div>
                <div class="actions">
                    <button type="button" class="bouton bouton-primaire" id="entree">Entree en stock</button>
                    <button type="button" class="bouton" id="ajustement">Ajustement</button>
                </div>
            </div>
            <div id="contenu-stock"></div>`;

        const contenu = cible.querySelector('#contenu-stock');

        cible.querySelectorAll('[data-onglet]').forEach((bouton) => {
            bouton.addEventListener('click', () => {
                etat.onglet = bouton.dataset.onglet;
                etat.page = 1;
                afficher(cible);
            });
        });

        cible.querySelector('#entree').addEventListener('click', () => formulaireEntree(cible));
        cible.querySelector('#ajustement').addEventListener('click', () => formulaireAjustement(cible));

        if (etat.onglet === 'niveaux') {
            await afficherNiveaux(contenu, cible);
        } else if (etat.onglet === 'alertes') {
            await afficherAlertes(contenu);
        } else {
            await afficherMouvements(contenu, cible);
        }
    } catch (exception) {
        erreur(exception, 'Stock indisponible');
        cible.innerHTML = '<div class="vide">Impossible de charger le stock.</div>';
    }
}

function classeOnglet(identifiant) {
    return etat.onglet === identifiant ? 'actif' : '';
}

async function afficherNiveaux(contenu, cible) {
    const page = await api.get('/api/stock', { page: etat.page, pageSize: 15, sort: 'available' });

    contenu.innerHTML = `
        ${tableau([
            { titre: 'Reference', rendu: (p) => `<code>${texte(p.sku)}</code>` },
            { titre: 'Produit', rendu: (p) => texte(p.name) },
            { titre: 'Physique', numerique: true, rendu: (p) => nombre(p.quantityOnHand) },
            { titre: 'Reserve', numerique: true, rendu: (p) => nombre(p.quantityReserved) },
            { titre: 'Disponible', numerique: true, rendu: (p) =>
                p.isLowStock
                    ? `<strong class="alerte-texte">${nombre(p.quantityAvailable)}</strong>`
                    : nombre(p.quantityAvailable) },
            { titre: 'Seuil', numerique: true, rendu: (p) => nombre(p.lowStockThreshold) }
        ], page.items, 'Aucun produit en stock.')}
        ${pagination(page, 'precedent', 'suivant')}`;

    brancherPagination(contenu, cible);
}

async function afficherAlertes(contenu) {
    const produits = await api.get('/api/stock/low-stock');

    contenu.innerHTML = `
        <p class="aide">
            Un produit remonte ici des que sa quantite disponible (physique moins reserve)
            atteint son seuil d'alerte.
        </p>
        ${tableau([
            { titre: 'Reference', rendu: (p) => `<code>${texte(p.sku)}</code>` },
            { titre: 'Produit', rendu: (p) => texte(p.name) },
            { titre: 'Disponible', numerique: true, rendu: (p) => nombre(p.quantityAvailable) },
            { titre: 'Seuil', numerique: true, rendu: (p) => nombre(p.lowStockThreshold) },
            { titre: 'A commander', numerique: true, rendu: (p) =>
                nombre(Math.max(p.lowStockThreshold * 2 - p.quantityAvailable, 0)) }
        ], produits, 'Aucune alerte : tous les stocks sont au-dessus de leur seuil.')}`;
}

async function afficherMouvements(contenu, cible) {
    const page = await api.get('/api/stock/movements', { page: etat.page, pageSize: 15 });

    contenu.innerHTML = `
        <p class="aide">
            Chaque operation laisse une trace : reception, inventaire, reservation d'une
            commande confirmee, liberation sur annulation, sortie a l'expedition.
        </p>
        ${tableau([
            { titre: 'Date', rendu: (m) => dateHeure(m.occurredAt) },
            { titre: 'Produit', rendu: (m) => `<code>${texte(m.sku)}</code> ${texte(m.productName)}` },
            { titre: 'Type', rendu: (m) => etiquette(m.type) },
            { titre: 'Quantite', numerique: true, rendu: (m) => nombre(m.quantity) },
            { titre: 'Physique apres', numerique: true, rendu: (m) => nombre(m.quantityOnHandAfter) },
            { titre: 'Reserve apres', numerique: true, rendu: (m) => nombre(m.quantityReservedAfter) },
            { titre: 'Reference', rendu: (m) => texte(m.reference || m.reason || '-') },
            { titre: 'Par', rendu: (m) => texte(m.performedBy) }
        ], page.items, 'Aucun mouvement enregistre.')}
        ${pagination(page, 'precedent', 'suivant')}`;

    brancherPagination(contenu, cible);
}

function brancherPagination(contenu, cible) {
    const precedent = contenu.querySelector('#precedent');
    const suivant = contenu.querySelector('#suivant');

    if (precedent) {
        precedent.addEventListener('click', () => { etat.page -= 1; afficher(cible); });
    }

    if (suivant) {
        suivant.addEventListener('click', () => { etat.page += 1; afficher(cible); });
    }
}

/** Charge une fois la liste des produits actifs, pour les selecteurs. */
async function chargerProduits() {
    if (produitsActifs.length === 0) {
        const page = await api.get('/api/products', { pageSize: 100, isActive: true });
        produitsActifs = page.items;
    }

    return produitsActifs;
}

function selecteurProduit(identifiant) {
    return `
        <select id="${identifiant}" name="productId">
            ${produitsActifs.map((produit) =>
                `<option value="${produit.id}">${texte(produit.sku)} - ${texte(produit.name)}</option>`).join('')}
        </select>`;
}

async function formulaireEntree(cible) {
    await chargerProduits();

    modale('Entree en stock', `
        <p class="aide">Reception fournisseur ou retour client : la quantite physique augmente.</p>
        <div class="formulaire">
            <div class="champ"><label for="e-produit">Produit</label>${selecteurProduit('e-produit')}</div>
            <div class="champ">
                <label for="e-quantite">Quantite recue</label>
                <input type="number" id="e-quantite" name="quantity" min="1" step="1" value="1" required>
            </div>
            <div class="champ">
                <label for="e-motif">Motif ou bon de reception</label>
                <input id="e-motif" name="reason" placeholder="BR-2026-014">
            </div>
        </div>`, [
        {
            libelle: 'Enregistrer l\'entree',
            classe: 'bouton-primaire',
            action: async (fond) => {
                try {
                    const resultat = await api.post('/api/stock/entries', valeursFormulaire(fond));
                    succes('Entree enregistree', `${resultat.sku} : ${resultat.quantityOnHand} en stock`);
                    afficher(cible);
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);
}

async function formulaireAjustement(cible) {
    await chargerProduits();

    modale('Ajustement d\'inventaire', `
        <p class="aide">
            La quantite physique est redefinie a la valeur constatee. Elle ne peut pas
            descendre sous ce qui est deja reserve par des commandes confirmees.
        </p>
        <div class="formulaire">
            <div class="champ"><label for="a-produit">Produit</label>${selecteurProduit('a-produit')}</div>
            <div class="champ">
                <label for="a-quantite">Quantite physique constatee</label>
                <input type="number" id="a-quantite" name="quantityOnHand" min="0" step="1" value="0" required>
            </div>
            <div class="champ">
                <label for="a-motif">Motif (obligatoire)</label>
                <input id="a-motif" name="reason" required placeholder="Inventaire du 09/11, casse constatee">
            </div>
        </div>`, [
        {
            libelle: 'Ajuster',
            classe: 'bouton-primaire',
            action: async (fond) => {
                try {
                    const resultat = await api.post('/api/stock/adjustments', valeursFormulaire(fond));
                    succes('Stock ajuste', `${resultat.sku} : ${resultat.quantityOnHand} en stock`);
                    afficher(cible);
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);
}
