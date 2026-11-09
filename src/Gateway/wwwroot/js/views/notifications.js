/** Notifications : suivi des evenements de livraison. */

import { api } from '../api.js';
import {
    chargement, dateHeure, erreur, etiquette, indicateur, nombre,
    pagination, succes, tableau, texte
} from '../ui.js';

export const titre = 'Notifications';
export const sousTitre = 'Evenements de livraison et alertes';

const etat = { page: 1, nonLues: false };

export async function afficher(cible) {
    chargement(cible, 'Chargement des notifications...');

    try {
        const [resume, page] = await Promise.all([
            api.get('/api/notifications/summary').catch(() => null),
            api.get('/api/notifications', {
                page: etat.page,
                pageSize: 15,
                unreadOnly: etat.nonLues ? true : ''
            })
        ]);

        cible.innerHTML = `
            ${resume ? `
                <div class="indicateurs">
                    ${indicateur('Total', nombre(resume.total))}
                    ${indicateur('Non lues', nombre(resume.unread))}
                    ${indicateur('Avertissements', nombre(resume.warnings), 'non lus')}
                </div>` : ''}

            <div class="barre-outils">
                <div class="filtres">
                    <div class="champ champ-case">
                        <label for="f-non-lues">
                            <input type="checkbox" id="f-non-lues" ${etat.nonLues ? 'checked' : ''}>
                            Non lues seulement
                        </label>
                    </div>
                    <button type="button" class="bouton" id="appliquer">Filtrer</button>
                </div>
                <button type="button" class="bouton" id="tout-lu">Tout marquer comme lu</button>
            </div>

            ${tableau([
                { titre: 'Date', rendu: (n) => dateHeure(n.createdAt) },
                { titre: 'Gravite', rendu: (n) => etiquette(n.severity) },
                { titre: 'Sujet', rendu: (n) => `<code>${texte(n.subject)}</code>` },
                { titre: 'Message', rendu: (n) =>
                    `${n.isRead ? '' : '<strong>'}${texte(n.title)}${n.isRead ? '' : '</strong>'}` +
                    `<small>${texte(n.message)}</small>` },
                { titre: 'Etat', rendu: (n) => n.isRead ? 'Lue' : 'Non lue' },
                { titre: 'Actions', rendu: (n) => n.isRead
                    ? ''
                    : `<button type="button" class="bouton bouton-petit" data-lu="${n.id}">Marquer lue</button>` }
            ], page.items, 'Aucune notification.')}
            ${pagination(page, 'precedent', 'suivant')}`;

        brancher(cible);
    } catch (exception) {
        erreur(exception, 'Notifications indisponibles');
        cible.innerHTML = '<div class="vide">Impossible de charger les notifications.</div>';
    }
}

function brancher(cible) {
    cible.querySelector('#appliquer').addEventListener('click', () => {
        etat.nonLues = cible.querySelector('#f-non-lues').checked;
        etat.page = 1;
        afficher(cible);
    });

    cible.querySelector('#tout-lu').addEventListener('click', async () => {
        try {
            const resume = await api.post('/api/notifications/read-all');
            succes('Notifications marquees comme lues', `${resume.unread} non lue(s) restante(s)`);
            afficher(cible);
        } catch (exception) {
            erreur(exception);
        }
    });

    const precedent = cible.querySelector('#precedent');
    const suivant = cible.querySelector('#suivant');

    if (precedent) {
        precedent.addEventListener('click', () => { etat.page -= 1; afficher(cible); });
    }

    if (suivant) {
        suivant.addEventListener('click', () => { etat.page += 1; afficher(cible); });
    }

    cible.querySelectorAll('[data-lu]').forEach((bouton) => {
        bouton.addEventListener('click', async () => {
            try {
                await api.post(`/api/notifications/${bouton.dataset.lu}/read`);
                afficher(cible);
            } catch (exception) {
                erreur(exception);
            }
        });
    });
}
