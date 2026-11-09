/** Notifications : evenements recus des microservices et diffusion simulee. */

import { api, estBackOffice } from '../api.js';
import {
    chargement, tableau, etiquette, dateHeure, erreur, succes, modale,
    texte, pagination, indicateur, nombre
} from '../ui.js';

export const titre = 'Notifications';
export const sousTitre = 'Evenements d\'integration recus des microservices et diffusion simulee';

const etat = { page: 1, type: '', gravite: '', nonLues: '', recherche: '' };

export async function afficher(cible) {
    chargement(cible);

    try {
        const [page, resume] = await Promise.all([
            api.get('/api/notifications', {
                page: etat.page,
                pageSize: 15,
                eventType: etat.type,
                severity: etat.gravite,
                unreadOnly: etat.nonLues,
                search: etat.recherche
            }),
            api.get('/api/notifications/summary').catch(() => null)
        ]);

        const types = Object.keys(resume?.byEventType || {});

        cible.innerHTML = `
            ${resume ? `
                <div class="grille-indicateurs">
                    ${indicateur('Total', nombre(resume.total), 'evenements recus')}
                    ${indicateur('Non lues', nombre(resume.unread))}
                    ${indicateur('Avertissements', nombre(resume.warning))}
                    ${indicateur('Critiques', nombre(resume.critical))}
                    ${indicateur('Diffusions en echec', nombre(resume.failed))}
                </div>` : ''}

            <div class="carte">
                <div class="barre-filtres">
                    <div class="champ">
                        <label for="f-recherche">Recherche</label>
                        <input id="f-recherche" type="search" placeholder="titre, message, sujet" value="${texte(etat.recherche)}" />
                    </div>
                    <div class="champ">
                        <label for="f-type">Type d'evenement</label>
                        <select id="f-type">
                            <option value="">Tous</option>
                            ${types.map((t) => `<option value="${t}" ${etat.type === t ? 'selected' : ''}>${texte(t)}</option>`).join('')}
                        </select>
                    </div>
                    <div class="champ">
                        <label for="f-gravite">Gravite</label>
                        <select id="f-gravite">
                            <option value="">Toutes</option>
                            <option value="Info" ${etat.gravite === 'Info' ? 'selected' : ''}>Info</option>
                            <option value="Warning" ${etat.gravite === 'Warning' ? 'selected' : ''}>Avertissement</option>
                            <option value="Critical" ${etat.gravite === 'Critical' ? 'selected' : ''}>Critique</option>
                        </select>
                    </div>
                    <div class="champ">
                        <label for="f-lues">Lecture</label>
                        <select id="f-lues">
                            <option value="">Toutes</option>
                            <option value="true" ${etat.nonLues === 'true' ? 'selected' : ''}>Non lues</option>
                        </select>
                    </div>
                    <button type="button" class="bouton" id="b-filtrer">Filtrer</button>
                    ${estBackOffice() ? '<button type="button" class="bouton" id="b-tout-lu">Tout marquer lu</button>' : ''}
                </div>

                ${tableau([
                    { titre: 'Recue le', rendu: (n) => dateHeure(n.receivedAt) },
                    { titre: 'Gravite', rendu: (n) => etiquette(n.severity) },
                    { titre: 'Type', rendu: (n) => `<code>${texte(n.eventType)}</code>` },
                    { titre: 'Emetteur', rendu: (n) => texte(n.source) },
                    { titre: 'Titre', rendu: (n) => texte(n.title) },
                    { titre: 'Canal', rendu: (n) => texte(n.channel) },
                    { titre: 'Diffusion', rendu: (n) => etiquette(n.status) },
                    { titre: 'Actions', rendu: (n) => `
                        <div class="actions-ligne">
                            <button type="button" class="bouton bouton-petit" data-detail="${n.id}">Voir</button>
                            ${n.status !== 'Lue' ? `<button type="button" class="bouton bouton-petit" data-lu="${n.id}">Lu</button>` : ''}
                        </div>` }
                ], page.items, 'Aucune notification. Declenchez une commande pour voir arriver les evenements.')}
                ${pagination(page, 'b-precedent', 'b-suivant')}
            </div>`;

        brancher(cible, page);
    } catch (exception) {
        erreur(exception, 'Chargement des notifications impossible');
        cible.innerHTML = '<div class="carte"><div class="vide">Notifications indisponibles.</div></div>';
    }
}

function brancher(cible, page) {
    const recharger = () => afficher(cible);

    cible.querySelector('#b-filtrer').addEventListener('click', () => {
        etat.recherche = cible.querySelector('#f-recherche').value.trim();
        etat.type = cible.querySelector('#f-type').value;
        etat.gravite = cible.querySelector('#f-gravite').value;
        etat.nonLues = cible.querySelector('#f-lues').value;
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

    cible.querySelector('#b-tout-lu')?.addEventListener('click', async () => {
        try {
            const resultat = await api.post('/api/notifications/read-all');
            succes('Notifications marquees lues', `${resultat.updated} element(s)`);
            recharger();
        } catch (exception) {
            erreur(exception);
        }
    });

    cible.querySelectorAll('[data-lu]').forEach((bouton) => {
        bouton.addEventListener('click', async () => {
            try {
                await api.patch(`/api/notifications/${bouton.dataset.lu}/read`);
                recharger();
            } catch (exception) {
                erreur(exception);
            }
        });
    });

    cible.querySelectorAll('[data-detail]').forEach((bouton) => {
        bouton.addEventListener('click', () => {
            const notification = page.items.find((n) => n.id === bouton.dataset.detail);
            ouvrirDetail(notification, recharger);
        });
    });
}

function ouvrirDetail(notification, recharger) {
    const donnees = notification.data || {};

    modale(notification.title, `
        <dl class="definitions">
            <dt>Gravite</dt><dd>${etiquette(notification.severity)}</dd>
            <dt>Type</dt><dd><code>${texte(notification.eventType)}</code></dd>
            <dt>Emetteur</dt><dd>${texte(notification.source)}</dd>
            <dt>Sujet</dt><dd><code>${texte(notification.subject)}</code></dd>
            <dt>Canal</dt><dd>${texte(notification.channel)}</dd>
            <dt>Destinataire</dt><dd>${texte(notification.recipient || '-')}</dd>
            <dt>Diffusion</dt><dd>${etiquette(notification.status)} (${notification.sendAttempts} tentative(s))</dd>
            <dt>Emis le</dt><dd>${dateHeure(notification.occurredAt)}</dd>
            <dt>Recu le</dt><dd>${dateHeure(notification.receivedAt)}</dd>
            <dt>Correlation-ID</dt><dd><code>${texte(notification.correlationId || '-')}</code></dd>
        </dl>

        <h4 style="margin-top: 1rem;">Message</h4>
        <p>${texte(notification.message)}</p>

        ${notification.deliveryLog ? `
            <h4>Compte rendu de diffusion</h4>
            <p><code>${texte(notification.deliveryLog)}</code></p>` : ''}

        ${Object.keys(donnees).length > 0 ? `
            <h4>Donnees techniques de l'evenement</h4>
            ${tableau([
                { titre: 'Clef', rendu: (l) => `<code>${texte(l.cle)}</code>` },
                { titre: 'Valeur', rendu: (l) => texte(l.valeur) }
            ], Object.entries(donnees).map(([cle, valeur]) => ({ cle, valeur })))}` : ''}`,
        estBackOffice() && notification.status === 'Echouee'
            ? [{
                libelle: 'Relancer la diffusion',
                classe: 'bouton-principal',
                action: async () => {
                    try {
                        await api.post(`/api/notifications/${notification.id}/resend`);
                        succes('Diffusion relancee');
                        recharger();
                    } catch (exception) {
                        erreur(exception);

                        return false;
                    }
                }
            }]
            : []);
}
