/** Commandes : liste, fiche detaillee et transitions du cycle de vie. */

import { api, estBackOffice } from '../api.js';
import {
    chargement, dateHeure, erreur, etiquette, modale, montant,
    nombre, pagination, succes, tableau, texte, valeursFormulaire
} from '../ui.js';

export const titre = 'Commandes';
export const sousTitre = 'Brouillon, confirmation, preparation, expedition et livraison';

const etat = { page: 1, recherche: '', statut: '' };
let clients = [];
let produits = [];

export async function afficher(cible) {
    chargement(cible, 'Chargement des commandes...');

    try {
        const page = await api.get('/api/orders', {
            page: etat.page,
            pageSize: 15,
            search: etat.recherche,
            status: etat.statut
        });

        cible.innerHTML = `
            <div class="barre-outils">
                <div class="filtres">
                    <div class="champ">
                        <label for="f-recherche">Numero</label>
                        <input type="search" id="f-recherche" value="${texte(etat.recherche)}" placeholder="CMD-2026-">
                    </div>
                    <div class="champ">
                        <label for="f-statut">Etat</label>
                        <select id="f-statut">
                            <option value="">Tous</option>
                            ${['Brouillon', 'Confirmee', 'EnPreparation', 'Expediee', 'Livree', 'Annulee']
                                .map((statut) =>
                                    `<option value="${statut}" ${etat.statut === statut ? 'selected' : ''}>` +
                                    `${statut}</option>`).join('')}
                        </select>
                    </div>
                    <button type="button" class="bouton" id="appliquer">Filtrer</button>
                </div>
                <button type="button" class="bouton bouton-primaire" id="nouvelle">Nouvelle commande</button>
            </div>

            ${tableau([
                { titre: 'Numero', rendu: (c) => `<code>${texte(c.number)}</code>` },
                { titre: 'Client', rendu: (c) => texte(c.customerName) },
                { titre: 'Etat', rendu: (c) => etiquette(c.status) },
                { titre: 'Lignes', numerique: true, rendu: (c) => nombre(c.lines.length) },
                { titre: 'Total', numerique: true, rendu: (c) => montant(c.total, c.currency) },
                { titre: 'Creee le', rendu: (c) => dateHeure(c.createdAt) },
                { titre: 'Actions', rendu: (c) =>
                    `<button type="button" class="bouton bouton-petit" data-fiche="${c.id}">Ouvrir</button>` }
            ], page.items, 'Aucune commande ne correspond aux filtres.')}
            ${pagination(page, 'precedent', 'suivant')}`;

        brancher(cible);
    } catch (exception) {
        erreur(exception, 'Commandes indisponibles');
        cible.innerHTML = '<div class="vide">Impossible de charger les commandes.</div>';
    }
}

function brancher(cible) {
    cible.querySelector('#appliquer').addEventListener('click', () => {
        etat.recherche = cible.querySelector('#f-recherche').value.trim();
        etat.statut = cible.querySelector('#f-statut').value;
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

    cible.querySelector('#nouvelle').addEventListener('click', () => formulaireCommande(cible));

    cible.querySelectorAll('[data-fiche]').forEach((bouton) => {
        bouton.addEventListener('click', () => ouvrirFiche(cible, bouton.dataset.fiche));
    });
}

/* ------------------------------ Fiche ---------------------------------- */

async function ouvrirFiche(cible, identifiant) {
    try {
        const commande = await api.get(`/api/orders/${identifiant}`);

        const actions = [];

        if (commande.allowedTransitions.includes('Confirmee')) {
            actions.push({
                libelle: 'Confirmer',
                classe: 'bouton-primaire',
                action: () => transition(cible, `/api/orders/${commande.id}/confirm`,
                    'Commande confirmee', 'Stock reserve et prix figes.')
            });
        }

        if (estBackOffice() && commande.allowedTransitions.includes('EnPreparation')) {
            actions.push({
                libelle: 'Mettre en preparation',
                classe: 'bouton-primaire',
                action: () => transition(cible, `/api/orders/${commande.id}/prepare`, 'Commande en preparation')
            });
        }

        if (estBackOffice() && commande.allowedTransitions.includes('Expediee')) {
            actions.push({
                libelle: 'Expedier',
                classe: 'bouton-primaire',
                action: () => transition(cible, `/api/orders/${commande.id}/ship`,
                    'Commande expediee', 'Stock sorti et bordereau de livraison cree.')
            });
        }

        if (commande.allowedTransitions.includes('Annulee')) {
            actions.push({
                libelle: 'Annuler',
                classe: 'bouton-danger',
                action: () => annuler(cible, commande)
            });
        }

        modale(`Commande ${commande.number}`, fiche(commande), actions);
    } catch (exception) {
        erreur(exception);
    }
}

function fiche(commande) {
    return `
        <div class="fiche">
            <div class="fiche-entete">
                <div>
                    <strong>${texte(commande.customerName)}</strong>
                    <small>${texte(commande.shippingAddress)} - ${texte(commande.shippingCity)}</small>
                </div>
                <div>${etiquette(commande.status)}</div>
            </div>

            ${commande.lastError
                ? `<div class="encart-alerte">
                       <strong>Derniere confirmation refusee</strong>
                       ${texte(commande.lastError)}
                   </div>`
                : ''}

            ${tableau([
                { titre: 'Produit', rendu: (l) =>
                    l.sku ? `<code>${texte(l.sku)}</code> ${texte(l.productName)}` : `<small>prix non fige</small>` },
                { titre: 'Quantite', numerique: true, rendu: (l) => nombre(l.quantity) },
                { titre: 'Prix unitaire', numerique: true, rendu: (l) => montant(l.unitPrice, commande.currency) },
                { titre: 'Total', numerique: true, rendu: (l) => montant(l.lineTotal, commande.currency) }
            ], commande.lines, 'Aucune ligne.')}

            <div class="totaux">
                <div><span>Total hors taxes</span><strong>${montant(commande.subTotal, commande.currency)}</strong></div>
                <div><span>TVA (${(commande.vatRate * 100).toFixed(2)} %)</span>
                     <strong>${montant(commande.vatAmount, commande.currency)}</strong></div>
                <div class="grand"><span>Total a payer</span>
                     <strong>${montant(commande.total, commande.currency)}</strong></div>
            </div>

            <h4>Historique</h4>
            ${tableau([
                { titre: 'Date', rendu: (h) => dateHeure(h.changedAt) },
                { titre: 'Transition', rendu: (h) =>
                    `${h.fromStatus ? texte(h.fromStatus) + ' &rarr; ' : ''}${etiquette(h.toStatus)}` },
                { titre: 'Par', rendu: (h) => texte(h.changedBy) },
                { titre: 'Motif', rendu: (h) => texte(h.reason || '-') }
            ], commande.history, 'Aucun changement enregistre.')}
        </div>`;
}

async function transition(cible, chemin, titreSucces, detail = '') {
    try {
        const commande = await api.post(chemin);
        succes(titreSucces, `${commande.number} : ${detail}`.trim());
        afficher(cible);
    } catch (exception) {
        erreur(exception);

        return false;
    }
}

/**
 * Ouvre la demande d'annulation. Elle remplace la fiche a l'ecran : la fonction
 * renvoie donc false pour ne pas refermer la modale qui vient d'etre ouverte.
 */
function annuler(cible, commande) {
    modale(`Annuler ${commande.number}`, `
        <p>Le stock reserve sera libere. Cette operation est definitive.</p>
        <div class="champ">
            <label for="motif-annulation">Motif</label>
            <input id="motif-annulation" placeholder="Annulation demandee par le client">
        </div>`, [
        {
            libelle: 'Confirmer l\'annulation',
            classe: 'bouton-danger',
            action: async (fond) => {
                const motif = fond.querySelector('#motif-annulation').value.trim() || null;

                try {
                    await api.post(`/api/orders/${commande.id}/cancel`, { reason: motif });
                    succes('Commande annulee', commande.number);
                    afficher(cible);
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);

    return false;
}

/* --------------------------- Nouvelle commande -------------------------- */

async function formulaireCommande(cible) {
    try {
        if (estBackOffice() && clients.length === 0) {
            const page = await api.get('/api/customers', { pageSize: 100 });
            clients = page.items.filter((client) => client.isActive);
        }

        if (produits.length === 0) {
            const page = await api.get('/api/products', { pageSize: 100, isActive: true });
            produits = page.items;
        }
    } catch (exception) {
        erreur(exception);

        return;
    }

    const lignes = [];

    const fond = modale('Nouvelle commande', `
        <div class="formulaire">
            ${estBackOffice() ? `
                <div class="champ">
                    <label for="c-client">Client</label>
                    <select id="c-client" name="customerId">
                        ${clients.map((client) =>
                            `<option value="${client.id}">${texte(client.code)} - ${texte(client.name)}</option>`)
                            .join('')}
                    </select>
                </div>` : '<p class="aide">La commande sera creee pour votre compte client.</p>'}
            <div class="champ">
                <label for="c-adresse">Adresse de livraison</label>
                <input id="c-adresse" name="shippingAddress" placeholder="par defaut : adresse du client">
            </div>
            <div class="champ">
                <label for="c-ville">Ville</label>
                <input id="c-ville" name="shippingCity" placeholder="par defaut : ville du client">
            </div>
            <div class="champ">
                <label for="c-notes">Commentaire</label>
                <textarea id="c-notes" name="notes" rows="2"></textarea>
            </div>
        </div>

        <h4>Lignes</h4>
        <div class="ligne-ajout">
            <select id="c-produit">
                ${produits.map((produit) =>
                    `<option value="${produit.id}">${texte(produit.sku)} - ${texte(produit.name)} ` +
                    `(${produit.quantityAvailable} dispo)</option>`).join('')}
            </select>
            <input type="number" id="c-quantite" min="1" step="1" value="1">
            <button type="button" class="bouton" id="c-ajouter">Ajouter</button>
        </div>
        <div id="c-lignes"><div class="vide">Aucune ligne pour l'instant.</div></div>`, [
        {
            libelle: 'Creer le brouillon',
            classe: 'bouton-primaire',
            action: async (racine) => {
                if (lignes.length === 0) {
                    erreur(new Error('Ajoutez au moins une ligne.'), 'Commande incomplete');

                    return false;
                }

                const valeurs = valeursFormulaire(racine.querySelector('.formulaire'));

                try {
                    const creee = await api.post('/api/orders', { ...valeurs, lines: lignes });
                    succes('Brouillon cree', `${creee.number} : confirmez-la pour reserver le stock.`);
                    afficher(cible);
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);

    const zoneLignes = fond.querySelector('#c-lignes');

    const redessiner = () => {
        zoneLignes.innerHTML = tableau([
            { titre: 'Produit', rendu: (ligne) => texte(nomProduit(ligne.productId)) },
            { titre: 'Quantite', numerique: true, rendu: (ligne) => nombre(ligne.quantity) },
            { titre: '', rendu: (ligne) =>
                `<button type="button" class="bouton bouton-petit" data-retirer="${ligne.productId}">Retirer</button>` }
        ], lignes, 'Aucune ligne pour l\'instant.');

        zoneLignes.querySelectorAll('[data-retirer]').forEach((bouton) => {
            bouton.addEventListener('click', () => {
                const index = lignes.findIndex((ligne) => ligne.productId === bouton.dataset.retirer);

                if (index >= 0) {
                    lignes.splice(index, 1);
                    redessiner();
                }
            });
        });
    };

    fond.querySelector('#c-ajouter').addEventListener('click', () => {
        const productId = fond.querySelector('#c-produit').value;
        const quantity = Number(fond.querySelector('#c-quantite').value) || 1;
        const existante = lignes.find((ligne) => ligne.productId === productId);

        if (existante) {
            existante.quantity += quantity;
        } else {
            lignes.push({ productId, quantity });
        }

        redessiner();
    });
}

function nomProduit(identifiant) {
    const produit = produits.find((candidat) => candidat.id === identifiant);

    return produit ? `${produit.sku} - ${produit.name}` : identifiant;
}
