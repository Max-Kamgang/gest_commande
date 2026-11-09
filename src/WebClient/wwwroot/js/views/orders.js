/**
 * Commandes : saisie du brouillon, confirmation (saga), preparation, expedition,
 * annulation et suivi. Les actions proposees sont pilotees par le champ
 * allowedTransitions renvoye par l'API : l'interface ne devine jamais l'etat.
 */

import { api, estBackOffice, aRole } from '../api.js';
import {
    chargement, tableau, etiquette, montant, nombre, dateHeure, erreur, succes,
    modale, valeursFormulaire, texte, pagination, confirmer
} from '../ui.js';

export const titre = 'Commandes';
export const sousTitre = 'Cycle de vie complet : brouillon, confirmation, preparation, expedition, livraison';

const etat = { page: 1, statut: '', numero: '', tri: 'createdat_desc' };
let produits = [];
let clients = [];

export async function afficher(cible) {
    chargement(cible);

    try {
        if (produits.length === 0) {
            const page = await api.get('/api/products', { pageSize: 100, isActive: true });
            produits = page.items;
        }

        if (estBackOffice() && clients.length === 0) {
            const page = await api.get('/api/customers', { pageSize: 100 });
            clients = page.items;
        }

        const page = await api.get('/api/orders', {
            page: etat.page,
            pageSize: 15,
            status: etat.statut,
            number: etat.numero,
            sort: etat.tri
        });

        cible.innerHTML = `
            <div class="carte">
                <div class="barre-filtres">
                    <div class="champ">
                        <label for="f-numero">Numero</label>
                        <input id="f-numero" type="search" placeholder="CMD-2026-..." value="${texte(etat.numero)}" />
                    </div>
                    <div class="champ">
                        <label for="f-statut">Etat</label>
                        <select id="f-statut">
                            <option value="">Tous</option>
                            ${['Brouillon', 'Confirmee', 'EnPreparation', 'Expediee', 'Livree', 'Annulee']
                                .map((s) => `<option value="${s}" ${etat.statut === s ? 'selected' : ''}>${s}</option>`).join('')}
                        </select>
                    </div>
                    <div class="champ">
                        <label for="f-tri">Tri</label>
                        <select id="f-tri">
                            <option value="createdat_desc" ${etat.tri === 'createdat_desc' ? 'selected' : ''}>Plus recentes</option>
                            <option value="createdat" ${etat.tri === 'createdat' ? 'selected' : ''}>Plus anciennes</option>
                            <option value="total_desc" ${etat.tri === 'total_desc' ? 'selected' : ''}>Montant decroissant</option>
                            <option value="number" ${etat.tri === 'number' ? 'selected' : ''}>Numero</option>
                        </select>
                    </div>
                    <button type="button" class="bouton" id="b-filtrer">Filtrer</button>
                    ${estBackOffice() || aRole('Client')
                        ? '<button type="button" class="bouton bouton-principal" id="b-nouvelle">Nouvelle commande</button>'
                        : ''}
                </div>

                ${tableau([
                    { titre: 'Numero', rendu: (o) => `<code>${texte(o.number)}</code>` },
                    { titre: 'Client', rendu: (o) => texte(o.customerName || '-') },
                    { titre: 'Creee le', rendu: (o) => dateHeure(o.createdAt) },
                    { titre: 'Lignes', numerique: true, rendu: (o) => nombre(o.lines.length) },
                    { titre: 'Total TTC', numerique: true, rendu: (o) => montant(o.total, o.currency) },
                    { titre: 'Etat', rendu: (o) => etiquette(o.status) + (o.lastError
                        ? ' <span class="etiquette etiquette-danger" title="Derniere confirmation en echec">!</span>'
                        : '') },
                    { titre: 'Actions', rendu: (o) =>
                        `<div class="actions-ligne"><button type="button" class="bouton bouton-petit" data-detail="${o.id}">Ouvrir</button></div>` }
                ], page.items, 'Aucune commande ne correspond aux filtres.')}
                ${pagination(page, 'b-precedent', 'b-suivant')}
            </div>`;

        brancher(cible, page);
    } catch (exception) {
        erreur(exception, 'Chargement des commandes impossible');
        cible.innerHTML = '<div class="carte"><div class="vide">Commandes indisponibles.</div></div>';
    }
}

function brancher(cible, page) {
    const recharger = () => afficher(cible);

    cible.querySelector('#b-filtrer').addEventListener('click', () => {
        etat.numero = cible.querySelector('#f-numero').value.trim();
        etat.statut = cible.querySelector('#f-statut').value;
        etat.tri = cible.querySelector('#f-tri').value;
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

    cible.querySelector('#b-nouvelle')?.addEventListener('click', () => ouvrirCreation(recharger));

    cible.querySelectorAll('[data-detail]').forEach((bouton) => {
        bouton.addEventListener('click', () => ouvrirDetail(bouton.dataset.detail, recharger));
    });
}

/* ---------------------------- Creation --------------------------------- */

function ouvrirCreation(recharger) {
    const lignes = [{ productId: produits[0]?.id, quantity: 1 }];

    const rendreLignes = () => lignes.map((ligne, index) => `
        <tr>
            <td>
                <select data-ligne-produit="${index}">
                    ${produits.map((p) =>
                        `<option value="${p.id}" ${p.id === ligne.productId ? 'selected' : ''}>${texte(p.sku)} - ${texte(p.name)}</option>`).join('')}
                </select>
            </td>
            <td class="numerique">
                <input type="number" min="1" step="1" value="${ligne.quantity}" data-ligne-quantite="${index}" style="width: 90px;" />
            </td>
            <td class="numerique">
                <button type="button" class="bouton bouton-petit bouton-danger" data-ligne-retirer="${index}">Retirer</button>
            </td>
        </tr>`).join('');

    const corps = () => `
        <p class="sous-titre">
            La commande est creee en <strong>Brouillon</strong> : aucun prix n'est fige et
            aucun stock n'est engage avant la confirmation.
        </p>
        <div class="formulaire-grille">
            ${estBackOffice() ? `
                <div class="champ">
                    <label for="c-client">Client</label>
                    <select id="c-client" name="customerId">
                        ${clients.map((c) => `<option value="${c.id}">${texte(c.code)} - ${texte(c.name)}</option>`).join('')}
                    </select>
                </div>` : ''}
            <div class="champ">
                <label for="c-ville">Ville de livraison</label>
                <input id="c-ville" name="shippingCity" placeholder="Douala" />
            </div>
            <div class="champ" style="grid-column: 1 / -1;">
                <label for="c-adresse">Adresse de livraison</label>
                <input id="c-adresse" name="shippingAddress" placeholder="Rue Joss, Bonanjo" />
            </div>
            <div class="champ" style="grid-column: 1 / -1;">
                <label for="c-notes">Notes</label>
                <textarea id="c-notes" name="notes" placeholder="Livraison avant 16h"></textarea>
            </div>
        </div>

        <h4 style="margin-top: 1.2rem;">Lignes</h4>
        <div class="table-enveloppe">
            <table class="tableau">
                <thead><tr><th>Produit</th><th class="numerique">Quantite</th><th></th></tr></thead>
                <tbody id="corps-lignes">${rendreLignes()}</tbody>
            </table>
        </div>
        <button type="button" class="bouton bouton-petit" id="b-ajouter-ligne" style="margin-top: .6rem;">
            Ajouter une ligne
        </button>`;

    const fond = modale('Nouvelle commande', corps(), [
        {
            libelle: 'Creer le brouillon',
            classe: 'bouton-principal',
            action: async (racine) => {
                const valeurs = valeursFormulaire(racine);

                const charge = {
                    customerId: valeurs.customerId || undefined,
                    shippingAddress: valeurs.shippingAddress,
                    shippingCity: valeurs.shippingCity,
                    notes: valeurs.notes,
                    lines: lignes.filter((l) => l.productId && l.quantity > 0)
                };

                if (charge.lines.length === 0) {
                    erreur(new Error('Ajoutez au moins une ligne a la commande.'), 'Commande vide');

                    return false;
                }

                try {
                    // Cle d'idempotence : un double clic ne cree pas deux commandes.
                    const creee = await api.post('/api/orders', charge, {
                        idempotencyKey: `web-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`
                    });

                    succes('Commande creee', `${creee.number} en brouillon`);
                    recharger();
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        }
    ]);

    const rebrancher = () => {
        const corpsLignes = fond.querySelector('#corps-lignes');
        corpsLignes.innerHTML = rendreLignes();
        brancherLignes();
    };

    const brancherLignes = () => {
        fond.querySelectorAll('[data-ligne-produit]').forEach((champ) => {
            champ.addEventListener('change', () => {
                lignes[Number(champ.dataset.ligneProduit)].productId = champ.value;
            });
        });

        fond.querySelectorAll('[data-ligne-quantite]').forEach((champ) => {
            champ.addEventListener('input', () => {
                lignes[Number(champ.dataset.ligneQuantite)].quantity = Number(champ.value) || 1;
            });
        });

        fond.querySelectorAll('[data-ligne-retirer]').forEach((bouton) => {
            bouton.addEventListener('click', () => {
                if (lignes.length === 1) {
                    return;
                }

                lignes.splice(Number(bouton.dataset.ligneRetirer), 1);
                rebrancher();
            });
        });
    };

    fond.querySelector('#b-ajouter-ligne').addEventListener('click', () => {
        lignes.push({ productId: produits[0]?.id, quantity: 1 });
        rebrancher();
    });

    brancherLignes();
}

/* ------------------------------ Detail --------------------------------- */

async function ouvrirDetail(identifiant, recharger) {
    try {
        const [commande, historique] = await Promise.all([
            api.get(`/api/orders/${identifiant}`),
            api.get(`/api/orders/${identifiant}/history`).catch(() => [])
        ]);

        const actions = construireActions(commande, recharger);

        modale(`Commande ${commande.number}`, `
            ${commande.lastError ? `
                <div class="encart encart-danger">
                    <strong>Derniere confirmation en echec</strong><br />
                    ${texte(commande.lastError)}
                    <br /><small>${dateHeure(commande.lastErrorAt)}</small>
                </div>` : ''}

            <dl class="definitions">
                <dt>Etat</dt><dd>${etiquette(commande.status)}</dd>
                <dt>Client</dt><dd>${texte(commande.customerName || '-')} (${texte(commande.customerCode || '-')})</dd>
                <dt>Livraison</dt><dd>${texte(commande.shippingAddress || '-')}, ${texte(commande.shippingCity || '-')}</dd>
                <dt>Creee le</dt><dd>${dateHeure(commande.createdAt)} par ${texte(commande.createdBy)}</dd>
                ${commande.confirmedAt ? `<dt>Confirmee le</dt><dd>${dateHeure(commande.confirmedAt)}</dd>` : ''}
                ${commande.shippedAt ? `<dt>Expediee le</dt><dd>${dateHeure(commande.shippedAt)}</dd>` : ''}
                ${commande.deliveredAt ? `<dt>Livree le</dt><dd>${dateHeure(commande.deliveredAt)}</dd>` : ''}
                ${commande.cancelledAt ? `<dt>Annulee le</dt><dd>${dateHeure(commande.cancelledAt)} - ${texte(commande.cancellationReason || '')}</dd>` : ''}
                <dt>Transitions</dt><dd>${commande.allowedTransitions.length
                    ? commande.allowedTransitions.map((t) => etiquette(t)).join(' ')
                    : '<em>etat final</em>'}</dd>
            </dl>

            <h4 style="margin-top: 1.1rem;">Lignes</h4>
            ${tableau([
                { titre: 'Reference', rendu: (l) => l.sku ? `<code>${texte(l.sku)}</code>` : '<em>non figee</em>' },
                { titre: 'Produit', rendu: (l) => texte(l.productName || nomProduit(l.productId)) },
                { titre: 'Qte', numerique: true, rendu: (l) => nombre(l.quantity) },
                { titre: 'Prix unitaire', numerique: true, rendu: (l) => l.unitPrice ? montant(l.unitPrice, commande.currency) : '-' },
                { titre: 'Total', numerique: true, rendu: (l) => l.lineTotal ? montant(l.lineTotal, commande.currency) : '-' }
            ], commande.lines)}

            <dl class="definitions" style="margin-top: .9rem;">
                <dt>Total HT</dt><dd>${montant(commande.subTotal, commande.currency)}</dd>
                <dt>TVA (${(commande.vatRate * 100).toFixed(2)} %)</dt><dd>${montant(commande.vatAmount, commande.currency)}</dd>
                <dt>Total TTC</dt><dd><strong>${montant(commande.total, commande.currency)}</strong></dd>
            </dl>

            <h4 style="margin-top: 1.1rem;">Historique des etats</h4>
            <ul class="liste-suivi">
                ${historique.map((h) => `
                    <li>
                        <strong>${texte(h.fromStatus || 'creation')} &rarr; ${texte(h.toStatus)}</strong>
                        ${h.reason ? `<br />${texte(h.reason)}` : ''}
                        <span class="horodatage">${dateHeure(h.changedAt)} &middot; ${texte(h.changedBy)}</span>
                    </li>`).join('') || '<li>Aucun changement enregistre.</li>'}
            </ul>`, actions);
    } catch (exception) {
        erreur(exception, 'Ouverture de la commande impossible');
    }
}

function construireActions(commande, recharger) {
    const actions = [];
    const transitions = commande.allowedTransitions || [];

    if (transitions.includes('Confirmee')) {
        actions.push({
            libelle: 'Confirmer (saga)',
            classe: 'bouton-principal',
            action: async () => {
                try {
                    const confirmee = await api.post(
                        `/api/orders/${commande.id}/confirm`,
                        undefined,
                        { idempotencyKey: `confirm-${commande.id}` });

                    succes('Commande confirmee',
                        `Prix figes et stock reserve. Total : ${montant(confirmee.total, confirmee.currency)}`);
                    recharger();
                } catch (exception) {
                    erreur(exception, 'Confirmation refusee');

                    return false;
                }
            }
        });
    }

    if (transitions.includes('EnPreparation') && estBackOffice()) {
        actions.push({
            libelle: 'Lancer la preparation',
            classe: 'bouton-principal',
            action: async () => {
                try {
                    await api.post(`/api/orders/${commande.id}/prepare`);
                    succes('Preparation lancee');
                    recharger();
                } catch (exception) {
                    erreur(exception);

                    return false;
                }
            }
        });
    }

    if (transitions.includes('Expediee') && estBackOffice()) {
        actions.push({
            libelle: 'Expedier',
            classe: 'bouton-succes',
            action: async () => {
                try {
                    await api.post(`/api/orders/${commande.id}/ship`);
                    succes('Commande expediee', 'Stock sorti et bordereau de livraison cree.');
                    recharger();
                } catch (exception) {
                    erreur(exception, 'Expedition interrompue');

                    return false;
                }
            }
        });
    }

    if (transitions.includes('Annulee')) {
        actions.push({
            libelle: 'Annuler',
            classe: 'bouton-danger',
            action: () => {
                confirmer(
                    `Annuler ${commande.number}`,
                    'Le stock reserve sera rendu disponible. Indiquez un motif :' +
                    '<div class="champ" style="margin-top:.6rem;">' +
                    '<input id="motif-annulation" name="reason" placeholder="Demande du client" /></div>',
                    'Confirmer l\'annulation',
                    async (racine) => {
                        const motif = racine.querySelector('#motif-annulation').value.trim();

                        if (!motif) {
                            erreur(new Error('Le motif est obligatoire.'), 'Motif manquant');

                            return false;
                        }

                        try {
                            await api.post(`/api/orders/${commande.id}/cancel`, { reason: motif });
                            succes('Commande annulee');
                            recharger();
                        } catch (exception) {
                            erreur(exception);

                            return false;
                        }
                    });

                return true;
            }
        });
    }

    return actions;
}

function nomProduit(identifiant) {
    return produits.find((p) => p.id === identifiant)?.name || identifiant.slice(0, 8);
}
