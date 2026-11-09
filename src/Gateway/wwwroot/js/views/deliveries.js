/** Livraisons : bordereaux, affectation des livreurs et suivi des tournees. */

import { api, estBackOffice } from '../api.js';
import {
    chargement, dateHeure, erreur, etiquette, modale, nombre,
    pagination, succes, tableau, texte
} from '../ui.js';

export const titre = 'Livraisons';
export const sousTitre = 'Bordereaux, affectation des livreurs et suivi';

const etat = { page: 1, recherche: '', statut: '', sansLivreur: false };
let livreurs = [];

export async function afficher(cible) {
    chargement(cible, 'Chargement des livraisons...');

    try {
        if (livreurs.length === 0) {
            livreurs = await api.get('/api/couriers').catch(() => []);
        }

        const page = await api.get('/api/deliveries', {
            page: etat.page,
            pageSize: 15,
            search: etat.recherche,
            status: etat.statut,
            unassigned: etat.sansLivreur ? true : ''
        });

        cible.innerHTML = `
            <div class="barre-outils">
                <div class="filtres">
                    <div class="champ">
                        <label for="f-recherche">Recherche</label>
                        <input type="search" id="f-recherche" value="${texte(etat.recherche)}"
                               placeholder="bordereau, commande ou client">
                    </div>
                    <div class="champ">
                        <label for="f-statut">Etat</label>
                        <select id="f-statut">
                            <option value="">Tous</option>
                            ${['EnAttente', 'Affectee', 'EnCours', 'Livree', 'Echouee', 'Annulee']
                                .map((statut) =>
                                    `<option value="${statut}" ${etat.statut === statut ? 'selected' : ''}>` +
                                    `${statut}</option>`).join('')}
                        </select>
                    </div>
                    ${estBackOffice() ? `
                        <div class="champ champ-case">
                            <label for="f-sans">
                                <input type="checkbox" id="f-sans" ${etat.sansLivreur ? 'checked' : ''}>
                                Sans livreur
                            </label>
                        </div>` : ''}
                    <button type="button" class="bouton" id="appliquer">Filtrer</button>
                </div>
            </div>

            ${tableau([
                { titre: 'Bordereau', rendu: (l) => `<code>${texte(l.reference)}</code>` },
                { titre: 'Commande', rendu: (l) => texte(l.orderNumber) },
                { titre: 'Client', rendu: (l) => `${texte(l.customerName)}<small>${texte(l.shippingCity)}</small>` },
                { titre: 'Etat', rendu: (l) => etiquette(l.status) },
                { titre: 'Livreur', rendu: (l) => texte(l.courierName || 'non affecte') },
                { titre: 'Tentatives', numerique: true, rendu: (l) => nombre(l.attemptCount) },
                { titre: 'Actions', rendu: (l) =>
                    `<button type="button" class="bouton bouton-petit" data-fiche="${l.id}">Ouvrir</button>` }
            ], page.items, 'Aucune livraison ne correspond aux filtres.')}
            ${pagination(page, 'precedent', 'suivant')}`;

        brancher(cible);
    } catch (exception) {
        erreur(exception, 'Livraisons indisponibles');
        cible.innerHTML = '<div class="vide">Impossible de charger les livraisons.</div>';
    }
}

function brancher(cible) {
    cible.querySelector('#appliquer').addEventListener('click', () => {
        etat.recherche = cible.querySelector('#f-recherche').value.trim();
        etat.statut = cible.querySelector('#f-statut').value;
        etat.sansLivreur = cible.querySelector('#f-sans')?.checked ?? false;
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

    cible.querySelectorAll('[data-fiche]').forEach((bouton) => {
        bouton.addEventListener('click', () => ouvrirFiche(cible, bouton.dataset.fiche));
    });
}

async function ouvrirFiche(cible, identifiant) {
    try {
        const livraison = await api.get(`/api/deliveries/${identifiant}`);
        const transitions = livraison.allowedTransitions;
        const actions = [];

        if (estBackOffice() && transitions.includes('Affectee')) {
            actions.push({
                libelle: livraison.courierId ? 'Reaffecter' : 'Affecter un livreur',
                classe: 'bouton-primaire',
                action: () => affecter(cible, livraison)
            });
        }

        if (transitions.includes('EnCours')) {
            actions.push({
                libelle: 'Enlevement effectue',
                classe: 'bouton-primaire',
                action: () => appeler(cible, `/api/deliveries/${livraison.id}/pickup`, {},
                    'Livraison en route', livraison.reference)
            });
        }

        if (transitions.includes('Livree')) {
            actions.push({
                libelle: 'Marquer livree',
                classe: 'bouton-primaire',
                action: () => terminer(cible, livraison)
            });
        }

        if (transitions.includes('Echouee')) {
            actions.push({
                libelle: 'Signaler un echec',
                classe: 'bouton-danger',
                action: () => signalerEchec(cible, livraison)
            });
        }

        if (estBackOffice() && livraison.status === 'Livree' && !livraison.orderNotified) {
            actions.push({
                libelle: 'Resynchroniser la commande',
                action: () => appeler(cible, `/api/deliveries/${livraison.id}/notify-order`, {},
                    'Commande mise a jour', livraison.orderNumber)
            });
        }

        modale(`Livraison ${livraison.reference}`, fiche(livraison), actions);
    } catch (exception) {
        erreur(exception);
    }
}

function fiche(livraison) {
    return `
        <div class="fiche">
            <div class="fiche-entete">
                <div>
                    <strong>${texte(livraison.customerName)}</strong>
                    <small>${texte(livraison.shippingAddress)} - ${texte(livraison.shippingCity)}</small>
                    <small>Telephone : ${texte(livraison.customerPhone || 'non renseigne')}</small>
                </div>
                <div>${etiquette(livraison.status)}</div>
            </div>

            <p class="aide">
                Commande <code>${texte(livraison.orderNumber)}</code> -
                livreur ${texte(livraison.courierName || 'non affecte')}
                ${livraison.receivedBy ? ` - recu par ${texte(livraison.receivedBy)}` : ''}
            </p>

            ${livraison.failureReason
                ? `<div class="encart-alerte">
                       <strong>Derniere tentative echouee</strong>${texte(livraison.failureReason)}
                   </div>`
                : ''}

            ${tableau([
                { titre: 'Produit', rendu: (l) => `<code>${texte(l.sku)}</code> ${texte(l.productName)}` },
                { titre: 'Quantite', numerique: true, rendu: (l) => nombre(l.quantity) }
            ], livraison.lines, 'Bordereau vide.')}

            <h4>Suivi</h4>
            ${tableau([
                { titre: 'Date', rendu: (t) => dateHeure(t.changedAt) },
                { titre: 'Transition', rendu: (t) =>
                    `${t.fromStatus ? texte(t.fromStatus) + ' &rarr; ' : ''}${etiquette(t.toStatus)}` },
                { titre: 'Par', rendu: (t) => texte(t.changedBy) },
                { titre: 'Commentaire', rendu: (t) => texte(t.comment || '-') }
            ], livraison.tracking, 'Aucune etape enregistree.')}
        </div>`;
}

async function appeler(cible, chemin, corps, titreSucces, detail) {
    try {
        await api.post(chemin, corps);
        succes(titreSucces, detail);
        afficher(cible);
    } catch (exception) {
        erreur(exception);

        return false;
    }
}

function affecter(cible, livraison) {
    const disponibles = livreurs.filter((livreur) => livreur.isActive);

    modale(`Affecter ${livraison.reference}`, `
        <div class="formulaire">
            <div class="champ">
                <label for="l-livreur">Livreur</label>
                <select id="l-livreur">
                    ${disponibles.map((livreur) =>
                        `<option value="${livreur.id}" ${livreur.id === livraison.courierId ? 'selected' : ''}>` +
                        `${texte(livreur.code)} - ${texte(livreur.fullName)} (${livreur.vehicle}, ` +
                        `${livreur.activeDeliveries} en cours)</option>`).join('')}
                </select>
            </div>
        </div>`, [
        {
            libelle: 'Affecter',
            classe: 'bouton-primaire',
            action: async (fond) => {
                const courierId = fond.querySelector('#l-livreur').value;

                const resultat = await appeler(cible, `/api/deliveries/${livraison.id}/assign`,
                    { courierId }, 'Livreur affecte', livraison.reference);

                // Les compteurs de charge des livreurs ont change.
                livreurs = [];

                return resultat;
            }
        }
    ]);

    return false;
}

function terminer(cible, livraison) {
    modale(`Livraison ${livraison.reference} effectuee`, `
        <p>La commande ${texte(livraison.orderNumber)} passera a l'etat « Livree ».</p>
        <div class="formulaire">
            <div class="champ">
                <label for="l-recu">Recu par</label>
                <input id="l-recu" required placeholder="Nom de la personne presente">
            </div>
        </div>`, [
        {
            libelle: 'Enregistrer la remise',
            classe: 'bouton-primaire',
            action: async (fond) => {
                const receivedBy = fond.querySelector('#l-recu').value.trim();

                if (!receivedBy) {
                    erreur(new Error('Indiquez qui a receptionne la marchandise.'), 'Information manquante');

                    return false;
                }

                return appeler(cible, `/api/deliveries/${livraison.id}/complete`,
                    { receivedBy }, 'Livraison enregistree', livraison.reference);
            }
        }
    ]);

    return false;
}

function signalerEchec(cible, livraison) {
    modale(`Echec de ${livraison.reference}`, `
        <p>Le bordereau restera reaffectable pour une nouvelle tentative.</p>
        <div class="formulaire">
            <div class="champ">
                <label for="l-motif">Motif</label>
                <input id="l-motif" required placeholder="Client absent">
            </div>
        </div>`, [
        {
            libelle: 'Enregistrer l\'echec',
            classe: 'bouton-danger',
            action: async (fond) => {
                const reason = fond.querySelector('#l-motif').value.trim();

                if (!reason) {
                    erreur(new Error('Indiquez le motif de l\'echec.'), 'Information manquante');

                    return false;
                }

                return appeler(cible, `/api/deliveries/${livraison.id}/fail`,
                    { reason }, 'Echec enregistre', livraison.reference);
            }
        }
    ]);

    return false;
}
