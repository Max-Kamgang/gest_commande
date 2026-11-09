/**
 * Fonctions d'affichage partagees : messages, tableaux, modales, formatage.
 * Volontairement sans dependance externe.
 */

import { ErreurApi } from './api.js';

/* ------------------------------ Formatage ------------------------------ */

const formateurMonnaie = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 });
const formateurNombre = new Intl.NumberFormat('fr-FR');

export function montant(valeur, devise = 'XAF') {
    if (valeur === null || valeur === undefined) {
        return '-';
    }

    return `${formateurMonnaie.format(valeur)} ${devise}`;
}

export function nombre(valeur) {
    return valeur === null || valeur === undefined ? '-' : formateurNombre.format(valeur);
}

export function dateHeure(valeur) {
    if (!valeur) {
        return '-';
    }

    const date = new Date(valeur);

    return date.toLocaleString('fr-FR', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });
}

export function dateCourte(valeur) {
    if (!valeur) {
        return '-';
    }

    return new Date(valeur).toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: '2-digit' });
}

/** Echappe le texte avant insertion dans du HTML. */
export function texte(valeur) {
    if (valeur === null || valeur === undefined) {
        return '';
    }

    return String(valeur)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

/* ------------------------------ Messages ------------------------------- */

const COULEURS_ETAT = {
    Brouillon: 'neutre',
    Confirmee: 'info',
    EnPreparation: 'alerte',
    Expediee: 'info',
    Livree: 'succes',
    Annulee: 'danger',
    EnAttente: 'neutre',
    Affectee: 'info',
    EnCours: 'alerte',
    Echouee: 'danger',
    Active: 'info',
    Liberee: 'neutre',
    Consommee: 'succes',
    Recue: 'neutre',
    Envoyee: 'succes',
    Lue: 'neutre',
    Info: 'info',
    Warning: 'alerte',
    Critical: 'danger'
};

/** Etiquette coloree pour un statut metier. */
export function etiquette(valeur) {
    if (!valeur) {
        return '<span class="etiquette etiquette-neutre">-</span>';
    }

    const couleur = COULEURS_ETAT[valeur] || 'neutre';

    return `<span class="etiquette etiquette-${couleur}">${texte(valeur)}</span>`;
}

let compteurMessages = 0;

/** Affiche un message temporaire en bas a droite. */
export function message(titre, corps = '', variante = 'info', trace = null) {
    const zone = document.getElementById('zone-messages');
    const element = document.createElement('div');
    const identifiant = ++compteurMessages;

    element.className = `message ${variante}`;
    element.innerHTML =
        `<strong>${texte(titre)}</strong>${corps ? texte(corps) : ''}` +
        (trace ? `<span class="trace">Correlation-ID : ${texte(trace)}</span>` : '');

    zone.appendChild(element);

    setTimeout(() => element.remove(), variante === 'erreur' ? 9000 : 4500);

    return identifiant;
}

export function succes(titre, corps = '') {
    return message(titre, corps, 'succes');
}

/** Affiche une erreur d'API en exploitant le ProblemDetails. */
export function erreur(exception, titreParDefaut = 'Operation impossible') {
    if (exception instanceof ErreurApi) {
        const titre = exception.probleme?.title || titreParDefaut;
        const corps = exception.messageComplet;
        const variante = exception.statut === 503 ? 'alerte' : 'erreur';

        return message(
            `${titre}${exception.statut ? ` (${exception.statut})` : ''}`,
            corps,
            variante,
            exception.correlationId
        );
    }

    console.error(exception);

    return message(titreParDefaut, exception?.message || 'Erreur inattendue.', 'erreur');
}

/* ------------------------------ Tableaux ------------------------------- */

/**
 * Construit un tableau HTML.
 * @param {Array} colonnes [{ titre, cle, rendu, numerique }]
 * @param {Array} lignes donnees
 * @param {string} messageVide texte affiche si aucune ligne
 */
export function tableau(colonnes, lignes, messageVide = 'Aucun element.') {
    if (!lignes || lignes.length === 0) {
        return `<div class="vide">${texte(messageVide)}</div>`;
    }

    const entetes = colonnes
        .map((colonne) => `<th class="${colonne.numerique ? 'numerique' : ''}">${texte(colonne.titre)}</th>`)
        .join('');

    const corps = lignes
        .map((ligne) => {
            const cellules = colonnes
                .map((colonne) => {
                    const contenu = colonne.rendu
                        ? colonne.rendu(ligne)
                        : texte(ligne[colonne.cle]);

                    return `<td class="${colonne.numerique ? 'numerique' : ''}">${contenu}</td>`;
                })
                .join('');

            return `<tr>${cellules}</tr>`;
        })
        .join('');

    return `<div class="table-enveloppe"><table class="tableau"><thead><tr>${entetes}</tr></thead><tbody>${corps}</tbody></table></div>`;
}

/** Barre de pagination d'un PagedResult. */
export function pagination(page, idPrecedent, idSuivant) {
    if (!page || page.totalCount === 0) {
        return '';
    }

    const debut = (page.page - 1) * page.pageSize + 1;
    const fin = Math.min(page.page * page.pageSize, page.totalCount);

    return `
        <div class="pagination">
            <span>${debut}-${fin} sur ${nombre(page.totalCount)}</span>
            <button type="button" class="bouton bouton-petit" id="${idPrecedent}" ${page.hasPrevious ? '' : 'disabled'}>Precedent</button>
            <button type="button" class="bouton bouton-petit" id="${idSuivant}" ${page.hasNext ? '' : 'disabled'}>Suivant</button>
        </div>`;
}

/* ------------------------------- Modale -------------------------------- */

/**
 * Ouvre une modale.
 * @param {string} titre
 * @param {string} contenuHtml
 * @param {Array} actions [{ libelle, classe, action }] - action renvoyant true ferme la modale
 */
export function modale(titre, contenuHtml, actions = []) {
    const existante = document.querySelector('.fond-modale');

    if (existante) {
        existante.remove();
    }

    const fond = document.createElement('div');
    fond.className = 'fond-modale';

    const boutons = actions
        .map((bouton, index) =>
            `<button type="button" class="bouton ${bouton.classe || ''}" data-index="${index}">${texte(bouton.libelle)}</button>`)
        .join('');

    fond.innerHTML = `
        <div class="modale" role="dialog" aria-modal="true">
            <div class="modale-entete">
                <h3>${texte(titre)}</h3>
                <button type="button" class="fermer" aria-label="Fermer">&times;</button>
            </div>
            <div class="modale-corps">${contenuHtml}</div>
            <div class="modale-pied">
                <button type="button" class="bouton" data-annuler>Fermer</button>
                ${boutons}
            </div>
        </div>`;

    const fermer = () => fond.remove();

    fond.querySelector('.fermer').addEventListener('click', fermer);
    fond.querySelector('[data-annuler]').addEventListener('click', fermer);
    fond.addEventListener('click', (evenement) => {
        if (evenement.target === fond) {
            fermer();
        }
    });

    fond.querySelectorAll('.modale-pied .bouton[data-index]').forEach((bouton) => {
        bouton.addEventListener('click', async () => {
            const action = actions[Number(bouton.dataset.index)];
            bouton.disabled = true;

            try {
                const resultat = await action.action(fond);

                if (resultat !== false) {
                    fermer();
                }
            } finally {
                bouton.disabled = false;
            }
        });
    });

    document.body.appendChild(fond);

    const premier = fond.querySelector('input, select, textarea');

    if (premier) {
        premier.focus();
    }

    return fond;
}

/** Demande confirmation avant une action sensible. */
export function confirmer(titre, texteHtml, libelleAction, action, classe = 'bouton-danger') {
    return modale(titre, `<p>${texteHtml}</p>`, [
        { libelle: libelleAction, classe, action }
    ]);
}

/* ------------------------------- Divers -------------------------------- */

export function chargement(cible, libelle = 'Chargement...') {
    cible.innerHTML = `<div class="chargement">${texte(libelle)}</div>`;
}

/** Indicateur chiffre du tableau de bord. */
export function indicateur(libelle, valeur, detail = '') {
    return `
        <div class="indicateur">
            <div class="libelle">${texte(libelle)}</div>
            <div class="valeur">${texte(valeur)}</div>
            ${detail ? `<div class="detail">${texte(detail)}</div>` : ''}
        </div>`;
}

/** Recupere les valeurs d'un formulaire contenu dans un element. */
export function valeursFormulaire(racine) {
    const valeurs = {};

    racine.querySelectorAll('[name]').forEach((champ) => {
        if (champ.type === 'checkbox') {
            valeurs[champ.name] = champ.checked;
        } else if (champ.type === 'number') {
            valeurs[champ.name] = champ.value === '' ? null : Number(champ.value);
        } else {
            valeurs[champ.name] = champ.value.trim() === '' ? null : champ.value.trim();
        }
    });

    return valeurs;
}
