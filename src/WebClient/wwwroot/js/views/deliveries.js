/**
 * Livraisons : affectation d'un livreur (back-office) et progression des statuts.
 * Un compte de role Livreur ne voit que ses propres livraisons, c'est l'API qui
 * applique la restriction a partir du jeton.
 */

import { api, estBackOffice, aRole } from '../api.js';
import {
    chargement, tableau, etiquette, nombre, dateHeure, erreur, succes,
    modale, texte, pagination
} from '../ui.js';

export const titre = 'Livraisons';
export const sousTitre = 'Bordereaux, affectation des livreurs et suivi de tournee';

const etat = { page: 1, statut: '', recherche: '', nonAffectees: '' };
let livreurs = [];

export async function afficher(cible) {
    chargement(cible);

    try {
        if (livreurs.length === 0) {
            const page = await api.get('/api/couriers', { pageSize: 50 }).catch(() => ({ items: [] }));
            livreurs = page.items;
        }

        const page = await api.get('/api/deliveries', {
            page: etat.page,
            pageSize: 15,
            status: etat.statut,
            search: etat.recherche,
            unassignedOnly: etat.nonAffectees
        });

        cible.innerHTML = `
            <div class="carte">
                <div class="barre-filtres">
                    <div class="champ">
                        <label for="f-recherche">Recherche</label>
                        <input id="f-recherche" type="search" placeholder="LIV-..., CMD-..., client" value="${texte(etat.recherche)}" />
                    </div>
                    <div class="champ">
                        <label for="f-statut">Etat</label>
                        <select id="f-statut">
                            <option value="">Tous</option>
                            ${['EnAttente', 'Affectee', 'EnCours', 'Livree', 'Echouee', 'Annulee']
                                .map((s) => `<option value="${s}" ${etat.statut === s ? 'selected' : ''}>${s}</option>`).join('')}
                        </select>
                    </div>
                    ${estBackOffice() ? `
                        <div class="champ">
                            <label for="f-affectation">Affectation</label>
                            <select id="f-affectation">
                                <option value="">Toutes</option>
                                <option value="true" ${etat.nonAffectees === 'true' ? 'selected' : ''}>Sans livreur</option>
                            </select>
                        </div>` : ''}
                    <button type="button" class="bouton" id="b-filtrer">Filtrer</button>
                </div>

                ${tableau([
                    { titre: 'Reference', rendu: (d) => `<code>${texte(d.reference)}</code>` },
                    { titre: 'Commande', rendu: (d) => `<code>${texte(d.orderNumber)}</code>` },
                    { titre: 'Client', rendu: (d) => texte(d.customerName) },
                    { titre: 'Destination', rendu: (d) => texte(d.shippingCity || '-') },
                    { titre: 'Livreur', rendu: (d) => d.courierName
                        ? texte(d.courierName)
                        : '<span class="etiquette etiquette-neutre">non affecte</span>' },
                    { titre: 'Colis', numerique: true, rendu: (d) => nombre(d.lines.length) },
                    { titre: 'Etat', rendu: (d) => etiquette(d.status)
                        + (d.attemptCount > 0 ? ` <small>(${d.attemptCount} echec(s))</small>` : '') },
                    { titre: 'Actions', rendu: (d) =>
                        `<div class="actions-ligne"><button type="button" class="bouton bouton-petit" data-detail="${d.id}">Ouvrir</button></div>` }
                ], page.items, aRole('Livreur') && !estBackOffice()
                    ? 'Aucune livraison ne vous est affectee pour le moment.'
                    : 'Aucune livraison ne correspond aux filtres.')}
                ${pagination(page, 'b-precedent', 'b-suivant')}
            </div>`;

        brancher(cible, page);
    } catch (exception) {
        erreur(exception, 'Chargement des livraisons impossible');
        cible.innerHTML = '<div class="carte"><div class="vide">Livraisons indisponibles.</div></div>';
    }
}

function brancher(cible, page) {
    const recharger = () => afficher(cible);

    cible.querySelector('#b-filtrer').addEventListener('click', () => {
        etat.recherche = cible.querySelector('#f-recherche').value.trim();
        etat.statut = cible.querySelector('#f-statut').value;
        etat.nonAffectees = cible.querySelector('#f-affectation')?.value || '';
        etat.page = 1;
        recharger();
    });

    cible.querySelector('#b-precedent')?.addEventListener('click', () => {
        etat.page = Math.max(1, page.page - 1);
        recharger();
    });

    cible.querySelector('#b-suivant')?.addEventListener('click', () => {
        etat.page = page.page + 1;
        recharger();
    });

    cible.querySelectorAll('[data-detail]').forEach((bouton) => {
        bouton.addEventListener('click', () => ouvrirDetail(bouton.dataset.detail, recharger));
    });
}

async function ouvrirDetail(identifiant, recharger) {
    try {
        const [livraison, suivi] = await Promise.all([
            api.get(`/api/deliveries/${identifiant}`),
            api.get(`/api/deliveries/${identifiant}/tracking`).catch(() => [])
        ]);

        modale(`Livraison ${livraison.reference}`, `
            ${livraison.failureReason ? `
                <div class="encart encart-alerte">
                    <strong>Derniere tentative en echec</strong><br />${texte(livraison.failureReason)}
                </div>` : ''}
            ${livraison.status === 'Livree' && !livraison.orderNotified ? `
                <div class="encart encart-danger">
                    <strong>Commande non mise a jour</strong><br />
                    order-service n'a pas pu etre informe de cette livraison.
                    Un gestionnaire peut relancer la notification.
                </div>` : ''}

            <dl class="definitions">
                <dt>Etat</dt><dd>${etiquette(livraison.status)}</dd>
                <dt>Commande</dt><dd><code>${texte(livraison.orderNumber)}</code></dd>
                <dt>Client</dt><dd>${texte(livraison.customerName)} ${livraison.customerPhone ? `&middot; ${texte(livraison.customerPhone)}` : ''}</dd>
                <dt>Adresse</dt><dd>${texte(livraison.shippingAddress || '-')}, ${texte(livraison.shippingCity || '-')}</dd>
                <dt>Livreur</dt><dd>${texte(livraison.courierName || 'non affecte')} ${livraison.courierPhone ? `&middot; ${texte(livraison.courierPhone)}` : ''}</dd>
                <dt>Creee le</dt><dd>${dateHeure(livraison.createdAt)}</dd>
                ${livraison.assignedAt ? `<dt>Affectee le</dt><dd>${dateHeure(livraison.assignedAt)}</dd>` : ''}
                ${livraison.pickedUpAt ? `<dt>Enlevee le</dt><dd>${dateHeure(livraison.pickedUpAt)}</dd>` : ''}
                ${livraison.deliveredAt ? `<dt>Livree le</dt><dd>${dateHeure(livraison.deliveredAt)} - recu par ${texte(livraison.receivedBy || '')}</dd>` : ''}
                <dt>Transitions</dt><dd>${livraison.allowedTransitions.length
                    ? livraison.allowedTransitions.map((t) => etiquette(t)).join(' ')
                    : '<em>etat final</em>'}</dd>
            </dl>

            <h4 style="margin-top: 1.1rem;">Contenu du bordereau</h4>
            ${tableau([
                { titre: 'Reference', rendu: (l) => l.sku ? `<code>${texte(l.sku)}</code>` : '-' },
                { titre: 'Produit', rendu: (l) => texte(l.productName || l.productId.slice(0, 8)) },
                { titre: 'Quantite', numerique: true, rendu: (l) => nombre(l.quantity) }
            ], livraison.lines)}

            <h4 style="margin-top: 1.1rem;">Suivi</h4>
            <ul class="liste-suivi">
                ${suivi.map((t) => `
                    <li>
                        <strong>${texte(t.fromStatus || 'creation')} &rarr; ${texte(t.toStatus)}</strong>
                        ${t.location ? ` &middot; ${texte(t.location)}` : ''}
                        ${t.comment ? `<br />${texte(t.comment)}` : ''}
                        <span class="horodatage">${dateHeure(t.changedAt)} &middot; ${texte(t.changedBy)}</span>
                    </li>`).join('') || '<li>Aucune etape enregistree.</li>'}
            </ul>`, construireActions(livraison, recharger));
    } catch (exception) {
        erreur(exception, 'Ouverture de la livraison impossible');
    }
}

function construireActions(livraison, recharger) {
    const actions = [];
    const transitions = livraison.allowedTransitions || [];
    const affectable = ['EnAttente', 'Affectee', 'Echouee'].includes(livraison.status);

    if (estBackOffice() && affectable && livreurs.length > 0) {
        actions.push({
            libelle: livraison.courierId ? 'Reaffecter' : 'Affecter un livreur',
            classe: 'bouton-principal',
            action: () => {
                ouvrirAffectation(livraison, recharger);

                return true;
            }
        });
    }

    const changerStatut = (statut, libelle, classe) => ({
        libelle,
        classe,
        action: () => {
            ouvrirChangementStatut(livraison, statut, recharger);

            return true;
        }
    });

    const peutAgir = estBackOffice()
        || (aRole('Livreur') && livraison.courierId);

    if (peutAgir) {
        if (transitions.includes('EnCours')) {
            actions.push(changerStatut('EnCours', 'Demarrer la tournee', 'bouton-principal'));
        }

        if (transitions.includes('Livree')) {
            actions.push(changerStatut('Livree', 'Marquer livree', 'bouton-succes'));
        }

        if (transitions.includes('Echouee')) {
            actions.push(changerStatut('Echouee', 'Declarer un echec', 'bouton-danger'));
        }
    }

    if (estBackOffice() && livraison.status === 'Livree' && !livraison.orderNotified) {
        actions.push({
            libelle: 'Relancer la notification',
            classe: 'bouton-principal',
            action: async () => {
                try {
                    await api.post(`/api/deliveries/${livraison.id}/notify-order`);
                    succes('Commande mise a jour');
                    recharger();
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        });
    }

    return actions;
}

function ouvrirAffectation(livraison, recharger) {
    modale(`Affecter ${livraison.reference}`, `
        <div class="formulaire-grille">
            <div class="champ">
                <label for="a-livreur">Livreur</label>
                <select id="a-livreur">
                    ${livreurs.filter((l) => l.isActive).map((l) =>
                        `<option value="${l.id}">${texte(l.code)} - ${texte(l.fullName)} (${l.activeDeliveries}/${l.maxConcurrentDeliveries} en charge)</option>`).join('')}
                </select>
            </div>
            <div class="champ">
                <label for="a-commentaire">Commentaire</label>
                <input id="a-commentaire" placeholder="Tournee du matin" />
            </div>
        </div>`, [
        {
            libelle: 'Affecter',
            classe: 'bouton-principal',
            action: async (racine) => {
                try {
                    await api.post(`/api/deliveries/${livraison.id}/assign`, {
                        courierId: racine.querySelector('#a-livreur').value,
                        comment: racine.querySelector('#a-commentaire').value.trim() || null
                    });

                    succes('Livreur affecte');
                    recharger();
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);
}

function ouvrirChangementStatut(livraison, statut, recharger) {
    const besoinDestinataire = statut === 'Livree';
    const besoinMotif = statut === 'Echouee';

    modale(`${livraison.reference} : passer a ${statut}`, `
        <div class="formulaire-grille">
            <div class="champ">
                <label for="s-lieu">Position</label>
                <input id="s-lieu" placeholder="Carrefour Ndokoti" />
            </div>
            ${besoinDestinataire ? `
                <div class="champ">
                    <label for="s-destinataire">Nom du destinataire (obligatoire)</label>
                    <input id="s-destinataire" placeholder="M. Kamgang" required />
                </div>` : ''}
            ${besoinMotif ? `
                <div class="champ">
                    <label for="s-motif">Motif de l'echec (obligatoire)</label>
                    <input id="s-motif" placeholder="Client absent" required />
                </div>` : ''}
            <div class="champ" style="grid-column: 1 / -1;">
                <label for="s-commentaire">Commentaire</label>
                <textarea id="s-commentaire"></textarea>
            </div>
        </div>`, [
        {
            libelle: `Passer a ${statut}`,
            classe: statut === 'Echouee' ? 'bouton-danger' : 'bouton-principal',
            action: async (racine) => {
                const charge = {
                    status: statut,
                    location: racine.querySelector('#s-lieu').value.trim() || null,
                    comment: racine.querySelector('#s-commentaire').value.trim() || null,
                    receivedBy: racine.querySelector('#s-destinataire')?.value.trim() || null,
                    failureReason: racine.querySelector('#s-motif')?.value.trim() || null
                };

                try {
                    const mise = await api.post(`/api/deliveries/${livraison.id}/status`, charge);

                    succes(`Livraison ${mise.status}`,
                        statut === 'Livree' ? 'La commande a ete passee a Livree.' : '');
                    recharger();
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);
}
