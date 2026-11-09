/** Catalogue : liste des produits, creation, modification, prix et activation. */

import { api, estBackOffice } from '../api.js';
import {
    chargement, erreur, etiquette, modale, montant, nombre,
    pagination, succes, tableau, texte, valeursFormulaire
} from '../ui.js';

export const titre = 'Produits';
export const sousTitre = 'Catalogue, prix et activation';

const etat = { page: 1, recherche: '', categorie: '', actif: '' };
let categories = [];

export async function afficher(cible) {
    chargement(cible, 'Chargement du catalogue...');

    try {
        if (categories.length === 0) {
            categories = await api.get('/api/categories');
        }

        const page = await api.get('/api/products', {
            page: etat.page,
            pageSize: 15,
            search: etat.recherche,
            categoryId: etat.categorie,
            isActive: etat.actif
        });

        cible.innerHTML = `
            <div class="barre-outils">
                <div class="filtres">
                    <div class="champ">
                        <label for="f-recherche">Recherche</label>
                        <input type="search" id="f-recherche" value="${texte(etat.recherche)}"
                               placeholder="reference ou nom">
                    </div>
                    <div class="champ">
                        <label for="f-categorie">Categorie</label>
                        <select id="f-categorie">
                            <option value="">Toutes</option>
                            ${categories.map((categorie) =>
                                `<option value="${categorie.id}" ${etat.categorie === categorie.id ? 'selected' : ''}>` +
                                `${texte(categorie.name)}</option>`).join('')}
                        </select>
                    </div>
                    <div class="champ">
                        <label for="f-actif">Etat</label>
                        <select id="f-actif">
                            <option value="">Tous</option>
                            <option value="true" ${etat.actif === 'true' ? 'selected' : ''}>Actifs</option>
                            <option value="false" ${etat.actif === 'false' ? 'selected' : ''}>Desactives</option>
                        </select>
                    </div>
                    <button type="button" class="bouton" id="appliquer">Filtrer</button>
                </div>
                ${estBackOffice() ? '<button type="button" class="bouton bouton-primaire" id="nouveau">Nouveau produit</button>' : ''}
            </div>

            ${tableau(colonnes(), page.items, 'Aucun produit ne correspond aux filtres.')}
            ${pagination(page, 'precedent', 'suivant')}`;

        brancher(cible);
    } catch (exception) {
        erreur(exception, 'Catalogue indisponible');
        cible.innerHTML = '<div class="vide">Impossible de charger le catalogue.</div>';
    }
}

function colonnes() {
    const liste = [
        { titre: 'Reference', rendu: (p) => `<code>${texte(p.sku)}</code>` },
        { titre: 'Produit', rendu: (p) => `${texte(p.name)}<small>${texte(p.categoryName)}</small>` },
        { titre: 'Prix', numerique: true, rendu: (p) => montant(p.unitPrice, p.currency) },
        { titre: 'Disponible', numerique: true, rendu: (p) =>
            p.isLowStock
                ? `<strong class="alerte-texte">${nombre(p.quantityAvailable)}</strong>`
                : nombre(p.quantityAvailable) },
        { titre: 'Etat', rendu: (p) => etiquette(p.isActive ? 'Actif' : 'Desactive') }
    ];

    if (estBackOffice()) {
        liste.push({
            titre: 'Actions',
            rendu: (p) => `
                <div class="actions">
                    <button type="button" class="bouton bouton-petit" data-modifier="${p.id}">Modifier</button>
                    <button type="button" class="bouton bouton-petit" data-prix="${p.id}">Prix</button>
                    <button type="button" class="bouton bouton-petit" data-actif="${p.id}">
                        ${p.isActive ? 'Desactiver' : 'Activer'}</button>
                </div>`
        });
    }

    return liste;
}

function brancher(cible) {
    cible.querySelector('#appliquer').addEventListener('click', () => {
        etat.recherche = cible.querySelector('#f-recherche').value.trim();
        etat.categorie = cible.querySelector('#f-categorie').value;
        etat.actif = cible.querySelector('#f-actif').value;
        etat.page = 1;
        afficher(cible);
    });

    const precedent = cible.querySelector('#precedent');
    const suivant = cible.querySelector('#suivant');

    if (precedent) {
        precedent.addEventListener('click', () => { etat.page -= 1; afficher(cible); });
    }

    if (suivant) {
        suivant.addEventListener('click', () => { etat.page += 1; afficher(cible); });
    }

    const nouveau = cible.querySelector('#nouveau');

    if (nouveau) {
        nouveau.addEventListener('click', () => formulaireProduit(cible, null));
    }

    cible.querySelectorAll('[data-modifier]').forEach((bouton) => {
        bouton.addEventListener('click', async () => {
            const produit = await api.get(`/api/products/${bouton.dataset.modifier}`);
            formulaireProduit(cible, produit);
        });
    });

    cible.querySelectorAll('[data-prix]').forEach((bouton) => {
        bouton.addEventListener('click', async () => {
            const produit = await api.get(`/api/products/${bouton.dataset.prix}`);
            formulairePrix(cible, produit);
        });
    });

    cible.querySelectorAll('[data-actif]').forEach((bouton) => {
        bouton.addEventListener('click', async () => {
            const produit = await api.get(`/api/products/${bouton.dataset.actif}`);

            try {
                await api.patch(`/api/products/${produit.id}/active`, { isActive: !produit.isActive });
                succes(produit.isActive ? 'Produit desactive' : 'Produit active', produit.sku);
                afficher(cible);
            } catch (exception) {
                erreur(exception);
            }
        });
    });
}

function formulaireProduit(cible, produit) {
    const creation = produit === null;

    modale(creation ? 'Nouveau produit' : `Modifier ${produit.sku}`, `
        <div class="formulaire">
            ${creation ? `
                <div class="champ">
                    <label for="p-sku">Reference</label>
                    <input id="p-sku" name="sku" required placeholder="INFO-PC-002">
                </div>` : ''}
            <div class="champ">
                <label for="p-nom">Libelle</label>
                <input id="p-nom" name="name" required value="${texte(produit?.name)}">
            </div>
            <div class="champ">
                <label for="p-description">Description</label>
                <textarea id="p-description" name="description" rows="2">${texte(produit?.description)}</textarea>
            </div>
            <div class="champ">
                <label for="p-categorie">Categorie</label>
                <select id="p-categorie" name="categoryId">
                    ${categories.map((categorie) =>
                        `<option value="${categorie.id}" ${produit?.categoryId === categorie.id ? 'selected' : ''}>` +
                        `${texte(categorie.name)}</option>`).join('')}
                </select>
            </div>
            ${creation ? `
                <div class="champ">
                    <label for="p-prix">Prix unitaire (XAF)</label>
                    <input type="number" id="p-prix" name="unitPrice" min="1" step="1" required>
                </div>
                <div class="champ">
                    <label for="p-stock">Stock initial</label>
                    <input type="number" id="p-stock" name="quantityOnHand" min="0" step="1" value="0">
                </div>` : ''}
            <div class="champ">
                <label for="p-seuil">Seuil d'alerte</label>
                <input type="number" id="p-seuil" name="lowStockThreshold" min="0" step="1"
                       value="${produit?.lowStockThreshold ?? 5}">
            </div>
        </div>`, [
        {
            libelle: creation ? 'Creer' : 'Enregistrer',
            classe: 'bouton-primaire',
            action: async (fond) => {
                const valeurs = valeursFormulaire(fond);

                try {
                    if (creation) {
                        const cree = await api.post('/api/products', valeurs);
                        succes('Produit cree', cree.sku);
                    } else {
                        await api.put(`/api/products/${produit.id}`, valeurs);
                        succes('Produit enregistre', produit.sku);
                    }

                    afficher(cible);
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);
}

function formulairePrix(cible, produit) {
    modale(`Prix de ${produit.sku}`, `
        <p>Prix actuel : <strong>${montant(produit.unitPrice, produit.currency)}</strong>.
        Les commandes deja confirmees gardent leur prix : il y est fige.</p>
        <div class="formulaire">
            <div class="champ">
                <label for="p-nouveau-prix">Nouveau prix unitaire (XAF)</label>
                <input type="number" id="p-nouveau-prix" name="unitPrice" min="1" step="1"
                       value="${produit.unitPrice}" required>
            </div>
        </div>`, [
        {
            libelle: 'Revaloriser',
            classe: 'bouton-primaire',
            action: async (fond) => {
                try {
                    await api.patch(`/api/products/${produit.id}/price`, valeursFormulaire(fond));
                    succes('Prix mis a jour', produit.sku);
                    afficher(cible);
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);
}
