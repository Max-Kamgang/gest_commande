/** Catalogue : consultation, creation, revalorisation et activation des produits. */

import { api, estBackOffice } from '../api.js';
import {
    chargement, tableau, etiquette, montant, nombre, erreur, succes, modale,
    valeursFormulaire, texte, pagination, confirmer
} from '../ui.js';

export const titre = 'Produits';
export const sousTitre = 'Catalogue : references, prix et disponibilite commerciale';

const etat = { page: 1, recherche: '', categorie: '', actif: '', tri: 'name' };
let categories = [];

export async function afficher(cible) {
    chargement(cible);

    try {
        if (categories.length === 0) {
            const pageCategories = await api.get('/api/categories', { pageSize: 100 });
            categories = pageCategories.items;
        }

        const page = await api.get('/api/products', {
            page: etat.page,
            pageSize: 15,
            search: etat.recherche,
            categoryId: etat.categorie,
            isActive: etat.actif,
            sort: etat.tri
        });

        cible.innerHTML = `
            <div class="carte">
                <div class="barre-filtres">
                    <div class="champ">
                        <label for="f-recherche">Recherche</label>
                        <input id="f-recherche" type="search" placeholder="reference, nom..." value="${texte(etat.recherche)}" />
                    </div>
                    <div class="champ">
                        <label for="f-categorie">Categorie</label>
                        <select id="f-categorie">
                            <option value="">Toutes</option>
                            ${categories.map((c) =>
                                `<option value="${c.id}" ${etat.categorie === c.id ? 'selected' : ''}>${texte(c.name)}</option>`).join('')}
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
                    <div class="champ">
                        <label for="f-tri">Tri</label>
                        <select id="f-tri">
                            <option value="name" ${etat.tri === 'name' ? 'selected' : ''}>Nom</option>
                            <option value="price_desc" ${etat.tri === 'price_desc' ? 'selected' : ''}>Prix decroissant</option>
                            <option value="price" ${etat.tri === 'price' ? 'selected' : ''}>Prix croissant</option>
                            <option value="sku" ${etat.tri === 'sku' ? 'selected' : ''}>Reference</option>
                            <option value="createdat_desc" ${etat.tri === 'createdat_desc' ? 'selected' : ''}>Plus recents</option>
                        </select>
                    </div>
                    <button type="button" class="bouton" id="b-filtrer">Filtrer</button>
                    ${estBackOffice() ? '<button type="button" class="bouton bouton-principal" id="b-nouveau">Nouveau produit</button>' : ''}
                </div>

                ${tableau(colonnes(), page.items, 'Aucun produit ne correspond aux filtres.')}
                ${pagination(page, 'b-precedent', 'b-suivant')}
            </div>`;

        brancher(cible, page);
    } catch (exception) {
        erreur(exception, 'Chargement du catalogue impossible');
        cible.innerHTML = '<div class="carte"><div class="vide">Catalogue indisponible.</div></div>';
    }
}

function colonnes() {
    const base = [
        { titre: 'Reference', rendu: (p) => `<code>${texte(p.sku)}</code>` },
        { titre: 'Designation', rendu: (p) => texte(p.name) },
        { titre: 'Categorie', rendu: (p) => texte(p.categoryName || '-') },
        { titre: 'Prix unitaire', numerique: true, rendu: (p) => montant(p.unitPrice, p.currency) },
        { titre: 'Etat', rendu: (p) => p.isActive
            ? '<span class="etiquette etiquette-succes">Actif</span>'
            : '<span class="etiquette etiquette-neutre">Desactive</span>' }
    ];

    if (estBackOffice()) {
        base.push({
            titre: 'Actions',
            rendu: (p) => `
                <div class="actions-ligne">
                    <button type="button" class="bouton bouton-petit" data-prix="${p.id}">Prix</button>
                    <button type="button" class="bouton bouton-petit" data-etat="${p.id}" data-actif="${p.isActive}">
                        ${p.isActive ? 'Desactiver' : 'Activer'}
                    </button>
                    <button type="button" class="bouton bouton-petit bouton-danger" data-supprimer="${p.id}">Supprimer</button>
                </div>`
        });
    }

    return base;
}

function brancher(cible, page) {
    const recharger = () => afficher(cible);

    cible.querySelector('#b-filtrer')?.addEventListener('click', () => {
        etat.recherche = cible.querySelector('#f-recherche').value.trim();
        etat.categorie = cible.querySelector('#f-categorie').value;
        etat.actif = cible.querySelector('#f-actif').value;
        etat.tri = cible.querySelector('#f-tri').value;
        etat.page = 1;
        recharger();
    });

    cible.querySelector('#f-recherche')?.addEventListener('keydown', (evenement) => {
        if (evenement.key === 'Enter') {
            cible.querySelector('#b-filtrer').click();
        }
    });

    cible.querySelector('#b-precedent')?.addEventListener('click', () => {
        etat.page = Math.max(1, page.page - 1);
        recharger();
    });

    cible.querySelector('#b-suivant')?.addEventListener('click', () => {
        etat.page = page.page + 1;
        recharger();
    });

    cible.querySelector('#b-nouveau')?.addEventListener('click', () => ouvrirCreation(recharger));

    cible.querySelectorAll('[data-prix]').forEach((bouton) => {
        bouton.addEventListener('click', () => {
            const produit = page.items.find((p) => p.id === bouton.dataset.prix);
            ouvrirChangementPrix(produit, recharger);
        });
    });

    cible.querySelectorAll('[data-etat]').forEach((bouton) => {
        bouton.addEventListener('click', async () => {
            const actif = bouton.dataset.actif === 'true';

            try {
                await api.patch(`/api/products/${bouton.dataset.etat}/status`, { isActive: !actif });
                succes(actif ? 'Produit desactive' : 'Produit active');
                recharger();
            } catch (exception) {
                erreur(exception);
            }
        });
    });

    cible.querySelectorAll('[data-supprimer]').forEach((bouton) => {
        bouton.addEventListener('click', () => {
            const produit = page.items.find((p) => p.id === bouton.dataset.supprimer);

            confirmer(
                'Supprimer le produit',
                `Supprimer definitivement <strong>${texte(produit.sku)}</strong> (${texte(produit.name)}) ?`,
                'Supprimer',
                async () => {
                    try {
                        await api.delete(`/api/products/${produit.id}`);
                        succes('Produit supprime');
                        recharger();
                    } catch (exception) {
                        erreur(exception);

                        return false;
                    }
                });
        });
    });
}

function ouvrirCreation(recharger) {
    const fond = modale('Nouveau produit', `
        <div class="formulaire-grille">
            <div class="champ">
                <label for="n-sku">Reference (SKU)</label>
                <input id="n-sku" name="sku" placeholder="RES-CAB-010" required />
            </div>
            <div class="champ">
                <label for="n-nom">Designation</label>
                <input id="n-nom" name="name" placeholder="Cable reseau Cat6 (5 m)" required />
            </div>
            <div class="champ">
                <label for="n-categorie">Categorie</label>
                <select id="n-categorie" name="categoryId">
                    ${categories.map((c) => `<option value="${c.id}">${texte(c.name)}</option>`).join('')}
                </select>
            </div>
            <div class="champ">
                <label for="n-prix">Prix unitaire (XAF)</label>
                <input id="n-prix" name="unitPrice" type="number" min="1" step="1" value="1000" required />
            </div>
            <div class="champ">
                <label for="n-unite">Unite</label>
                <input id="n-unite" name="unit" value="piece" />
            </div>
            <div class="champ" style="grid-column: 1 / -1;">
                <label for="n-description">Description</label>
                <textarea id="n-description" name="description"></textarea>
            </div>
        </div>`, [
        {
            libelle: 'Creer le produit',
            classe: 'bouton-principal',
            action: async (racine) => {
                const valeurs = valeursFormulaire(racine);

                try {
                    const cree = await api.post('/api/products', valeurs);
                    succes('Produit cree', `${cree.sku} - ${cree.name}`);
                    recharger();
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);

    return fond;
}

function ouvrirChangementPrix(produit, recharger) {
    modale(`Revaloriser ${produit.sku}`, `
        <p class="sous-titre">
            Prix actuel : <strong>${montant(produit.unitPrice, produit.currency)}</strong>.
            Le changement est historise ; les commandes deja confirmees conservent leur prix fige.
        </p>
        <div class="formulaire-grille">
            <div class="champ">
                <label for="p-prix">Nouveau prix (XAF)</label>
                <input id="p-prix" name="unitPrice" type="number" min="1" step="1" value="${produit.unitPrice}" />
            </div>
            <div class="champ">
                <label for="p-motif">Motif</label>
                <input id="p-motif" name="reason" placeholder="Hausse fournisseur" />
            </div>
        </div>`, [
        {
            libelle: 'Appliquer',
            classe: 'bouton-principal',
            action: async (racine) => {
                const valeurs = valeursFormulaire(racine);

                try {
                    await api.patch(`/api/products/${produit.id}/price`, valeurs);
                    succes('Prix mis a jour');
                    recharger();
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);
}
