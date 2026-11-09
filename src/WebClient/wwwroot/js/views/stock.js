/** Stock : niveaux par produit et entrepot, mouvements, entrees et ajustements. */

import { api, estBackOffice } from '../api.js';
import {
    chargement, tableau, nombre, dateHeure, erreur, succes, modale,
    valeursFormulaire, texte, pagination, etiquette
} from '../ui.js';

export const titre = 'Stock';
export const sousTitre = 'Niveaux par produit et entrepot, mouvements et alertes';

const etat = { page: 1, entrepot: '', stockBas: '', onglet: 'niveaux' };
let entrepots = [];
let produits = [];

export async function afficher(cible) {
    chargement(cible);

    try {
        if (entrepots.length === 0) {
            const page = await api.get('/api/warehouses', { pageSize: 50 });
            entrepots = page.items;
        }

        if (produits.length === 0) {
            const page = await api.get('/api/products', { pageSize: 100, isActive: true });
            produits = page.items;
        }

        cible.innerHTML = `
            <div class="carte">
                <div class="barre-filtres">
                    <div class="champ">
                        <label for="f-onglet">Affichage</label>
                        <select id="f-onglet">
                            <option value="niveaux" ${etat.onglet === 'niveaux' ? 'selected' : ''}>Niveaux de stock</option>
                            <option value="mouvements" ${etat.onglet === 'mouvements' ? 'selected' : ''}>Journal des mouvements</option>
                        </select>
                    </div>
                    <div class="champ">
                        <label for="f-entrepot">Entrepot</label>
                        <select id="f-entrepot">
                            <option value="">Tous</option>
                            ${entrepots.map((e) =>
                                `<option value="${e.id}" ${etat.entrepot === e.id ? 'selected' : ''}>${texte(e.code)} - ${texte(e.city)}</option>`).join('')}
                        </select>
                    </div>
                    ${etat.onglet === 'niveaux' ? `
                        <div class="champ">
                            <label for="f-bas">Filtre</label>
                            <select id="f-bas">
                                <option value="">Tout le stock</option>
                                <option value="true" ${etat.stockBas === 'true' ? 'selected' : ''}>Sous le seuil d'alerte</option>
                            </select>
                        </div>` : ''}
                    <button type="button" class="bouton" id="b-filtrer">Appliquer</button>
                    ${estBackOffice() ? `
                        <button type="button" class="bouton bouton-principal" id="b-entree">Entree de stock</button>
                        <button type="button" class="bouton" id="b-ajustement">Ajustement d'inventaire</button>` : ''}
                </div>
                <div id="zone-stock"></div>
            </div>`;

        await afficherContenu(cible);
        brancher(cible);
    } catch (exception) {
        erreur(exception, 'Chargement du stock impossible');
        cible.innerHTML = '<div class="carte"><div class="vide">Stock indisponible.</div></div>';
    }
}

async function afficherContenu(cible) {
    const zone = cible.querySelector('#zone-stock');
    chargement(zone);

    if (etat.onglet === 'mouvements') {
        const page = await api.get('/api/stock/movements', {
            page: etat.page,
            pageSize: 15,
            warehouseId: etat.entrepot
        });

        zone.innerHTML = tableau([
            { titre: 'Date', rendu: (m) => dateHeure(m.occurredAt) },
            { titre: 'Type', rendu: (m) => etiquetteMouvement(m.type) },
            { titre: 'Produit', rendu: (m) => texte(nomProduit(m.productId)) },
            { titre: 'Entrepot', rendu: (m) => texte(codeEntrepot(m.warehouseId)) },
            { titre: 'Qte', numerique: true, rendu: (m) => nombre(m.quantity) },
            { titre: 'Apres', numerique: true, rendu: (m) => `${nombre(m.quantityOnHandAfter)} / ${nombre(m.quantityReservedAfter)} res.` },
            { titre: 'Reference', rendu: (m) => m.reference ? `<code>${texte(m.reference)}</code>` : '-' },
            { titre: 'Par', rendu: (m) => texte(m.performedBy) }
        ], page.items, 'Aucun mouvement enregistre.')
            + pagination(page, 'b-precedent', 'b-suivant');

        brancherPagination(cible, page);

        return;
    }

    const page = etat.stockBas === 'true'
        ? await api.get('/api/stock/low-stock', { page: etat.page, pageSize: 15 })
        : await api.get('/api/stock', { page: etat.page, pageSize: 15, warehouseId: etat.entrepot, sort: 'available' });

    zone.innerHTML = tableau([
        { titre: 'Produit', rendu: (s) => texte(s.productLabel || nomProduit(s.productId)) },
        { titre: 'Entrepot', rendu: (s) => `${texte(s.warehouseCode)} <small>${texte(s.warehouseName)}</small>` },
        { titre: 'Physique', numerique: true, rendu: (s) => nombre(s.quantityOnHand) },
        { titre: 'Reserve', numerique: true, rendu: (s) => nombre(s.quantityReserved) },
        { titre: 'Disponible', numerique: true, rendu: (s) => `<strong>${nombre(s.quantityAvailable)}</strong>` },
        { titre: 'Seuil', numerique: true, rendu: (s) => nombre(s.lowStockThreshold) },
        { titre: 'Alerte', rendu: (s) => s.isLowStock
            ? '<span class="etiquette etiquette-alerte">Stock bas</span>'
            : '<span class="etiquette etiquette-succes">Correct</span>' },
        { titre: 'Mis a jour', rendu: (s) => dateHeure(s.updatedAt) }
    ], page.items, 'Aucune ligne de stock.')
        + pagination(page, 'b-precedent', 'b-suivant');

    brancherPagination(cible, page);
}

function etiquetteMouvement(type) {
    const couleurs = {
        Entree: 'succes',
        Sortie: 'danger',
        Ajustement: 'alerte',
        Reservation: 'info',
        LiberationReservation: 'neutre',
        SortieSurReservation: 'danger'
    };

    return `<span class="etiquette etiquette-${couleurs[type] || 'neutre'}">${texte(type)}</span>`;
}

function nomProduit(identifiant) {
    return produits.find((p) => p.id === identifiant)?.name || identifiant.slice(0, 8);
}

function codeEntrepot(identifiant) {
    return entrepots.find((e) => e.id === identifiant)?.code || identifiant.slice(0, 8);
}

function brancherPagination(cible, page) {
    cible.querySelector('#b-precedent')?.addEventListener('click', async () => {
        etat.page = Math.max(1, page.page - 1);
        await afficherContenu(cible);
    });

    cible.querySelector('#b-suivant')?.addEventListener('click', async () => {
        etat.page = page.page + 1;
        await afficherContenu(cible);
    });
}

function brancher(cible) {
    const recharger = () => afficher(cible);

    cible.querySelector('#b-filtrer').addEventListener('click', () => {
        etat.onglet = cible.querySelector('#f-onglet').value;
        etat.entrepot = cible.querySelector('#f-entrepot').value;
        etat.stockBas = cible.querySelector('#f-bas')?.value || '';
        etat.page = 1;
        recharger();
    });

    cible.querySelector('#f-onglet').addEventListener('change', () => {
        cible.querySelector('#b-filtrer').click();
    });

    cible.querySelector('#b-entree')?.addEventListener('click', () => ouvrirEntree(recharger));
    cible.querySelector('#b-ajustement')?.addEventListener('click', () => ouvrirAjustement(recharger));
}

function selecteurProduit(identifiant) {
    return `
        <select id="${identifiant}" name="productId">
            ${produits.map((p) => `<option value="${p.id}">${texte(p.sku)} - ${texte(p.name)}</option>`).join('')}
        </select>`;
}

function selecteurEntrepot(identifiant) {
    return `
        <select id="${identifiant}" name="warehouseId">
            ${entrepots.filter((e) => e.isActive)
                .map((e) => `<option value="${e.id}">${texte(e.code)} - ${texte(e.city)}</option>`).join('')}
        </select>`;
}

function ouvrirEntree(recharger) {
    modale('Entree de stock', `
        <p class="sous-titre">
            Reception fournisseur ou retour client. Si le couple produit / entrepot
            n'existe pas encore, la ligne de stock est creee automatiquement.
        </p>
        <div class="formulaire-grille">
            <div class="champ"><label for="e-produit">Produit</label>${selecteurProduit('e-produit')}</div>
            <div class="champ"><label for="e-entrepot">Entrepot</label>${selecteurEntrepot('e-entrepot')}</div>
            <div class="champ">
                <label for="e-quantite">Quantite</label>
                <input id="e-quantite" name="quantity" type="number" min="1" step="1" value="10" />
            </div>
            <div class="champ">
                <label for="e-seuil">Seuil d'alerte</label>
                <input id="e-seuil" name="lowStockThreshold" type="number" min="0" step="1" value="5" />
            </div>
            <div class="champ">
                <label for="e-reference">Reference (bon de reception)</label>
                <input id="e-reference" name="reference" placeholder="BR-2026-001" />
            </div>
            <div class="champ">
                <label for="e-motif">Motif</label>
                <input id="e-motif" name="reason" placeholder="Reception fournisseur" />
            </div>
        </div>`, [
        {
            libelle: 'Enregistrer l\'entree',
            classe: 'bouton-principal',
            action: async (racine) => {
                const valeurs = valeursFormulaire(racine);
                valeurs.productLabel = produits.find((p) => p.id === valeurs.productId)?.name || null;

                try {
                    const resultat = await api.post('/api/stock/entries', valeurs);
                    succes('Entree enregistree', `Nouveau disponible : ${resultat.quantityAvailable}`);
                    recharger();
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);
}

function ouvrirAjustement(recharger) {
    modale('Ajustement d\'inventaire', `
        <p class="sous-titre">
            La quantite physique est redefinie a la valeur comptee. Elle ne peut pas
            descendre sous la quantite deja reservee par des commandes confirmees.
        </p>
        <div class="formulaire-grille">
            <div class="champ"><label for="a-produit">Produit</label>${selecteurProduit('a-produit')}</div>
            <div class="champ"><label for="a-entrepot">Entrepot</label>${selecteurEntrepot('a-entrepot')}</div>
            <div class="champ">
                <label for="a-quantite">Quantite comptee</label>
                <input id="a-quantite" name="quantityOnHand" type="number" min="0" step="1" value="0" />
            </div>
            <div class="champ" style="grid-column: 1 / -1;">
                <label for="a-motif">Motif (obligatoire)</label>
                <input id="a-motif" name="reason" placeholder="Inventaire tournant du 09/11" required />
            </div>
        </div>`, [
        {
            libelle: 'Appliquer l\'ajustement',
            classe: 'bouton-principal',
            action: async (racine) => {
                const valeurs = valeursFormulaire(racine);

                try {
                    const resultat = await api.post('/api/stock/adjustments', valeurs);
                    succes('Ajustement applique', `Stock physique : ${resultat.quantityOnHand}`);
                    recharger();
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);
}
