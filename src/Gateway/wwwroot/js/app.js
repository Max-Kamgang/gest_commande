/**
 * Point d'entree de l'interface web : connexion, navigation par role,
 * routage par ancre (#/commandes...), indicateur de sante de la plateforme.
 */

import {
    chargerConfiguration, connecter, deconnecter, estConnecte, profil,
    aRole, api, surDeconnexion, metadonnees
} from './api.js';
import { erreur, texte, succes } from './ui.js';
import * as tableauDeBord from './views/dashboard.js';
import * as produits from './views/products.js';
import * as stock from './views/stock.js';
import * as commandes from './views/orders.js';
import * as livraisons from './views/deliveries.js';
import * as notifications from './views/notifications.js';

const VUES = {
    tableau: tableauDeBord,
    commandes,
    livraisons,
    produits,
    stock,
    notifications
};

/** Entrees de navigation, avec les roles autorises (null = tous les comptes connectes). */
const NAVIGATION = [
    { id: 'tableau', libelle: 'Tableau de bord', icone: '&#9632;', roles: null },
    // Un livreur n'a pas de fiche client : il n'a rien a voir dans les commandes.
    { id: 'commandes', libelle: 'Commandes', icone: '&#128722;', roles: ['Admin', 'Gestionnaire', 'Client'] },
    { id: 'livraisons', libelle: 'Livraisons', icone: '&#128666;', roles: ['Admin', 'Gestionnaire', 'Livreur'] },
    { id: 'produits', libelle: 'Produits', icone: '&#127991;', roles: null },
    { id: 'stock', libelle: 'Stock', icone: '&#128230;', roles: ['Admin', 'Gestionnaire'] },
    { id: 'notifications', libelle: 'Notifications', icone: '&#128276;', roles: ['Admin', 'Gestionnaire'] }
];

const dom = {
    connexion: document.getElementById('ecran-connexion'),
    application: document.getElementById('application'),
    formulaire: document.getElementById('formulaire-connexion'),
    erreurConnexion: document.getElementById('erreur-connexion'),
    navigation: document.getElementById('navigation'),
    utilisateur: document.getElementById('bloc-utilisateur'),
    titre: document.getElementById('titre-vue'),
    sousTitre: document.getElementById('sous-titre-vue'),
    vue: document.getElementById('vue'),
    sante: document.getElementById('indicateur-sante'),
    rafraichir: document.getElementById('bouton-rafraichir'),
    deconnexion: document.getElementById('bouton-deconnexion')
};

let vueCourante = null;

/* ------------------------------ Connexion ------------------------------ */

function afficherConnexion() {
    dom.application.hidden = true;
    dom.connexion.hidden = false;
    dom.formulaire.reset();
    document.getElementById('champ-identifiant').focus();
}

function accesVue(definition) {
    return definition.roles === null || aRole(...definition.roles);
}

function afficherApplication() {
    const utilisateur = profil();

    dom.connexion.hidden = true;
    dom.application.hidden = false;

    dom.utilisateur.innerHTML = `
        <strong>${texte(utilisateur.fullName)}</strong>
        <span>${texte(utilisateur.roles.join(', '))}</span>`;

    dom.navigation.innerHTML = NAVIGATION
        .filter(accesVue)
        .map((entree) =>
            `<button type="button" data-vue="${entree.id}">` +
            `<span class="icone">${entree.icone}</span>${texte(entree.libelle)}</button>`)
        .join('');

    dom.navigation.querySelectorAll('button').forEach((bouton) => {
        bouton.addEventListener('click', () => {
            window.location.hash = `#/${bouton.dataset.vue}`;
        });
    });

    route();
    verifierSante();
}

dom.formulaire.addEventListener('submit', async (evenement) => {
    evenement.preventDefault();
    dom.erreurConnexion.hidden = true;

    const identifiant = document.getElementById('champ-identifiant').value.trim();
    const motDePasse = document.getElementById('champ-motdepasse').value;
    const bouton = dom.formulaire.querySelector('button[type="submit"]');

    bouton.disabled = true;

    try {
        const utilisateur = await connecter(identifiant, motDePasse);
        succes('Connexion reussie', `Bienvenue ${utilisateur.fullName}`);

        if (!window.location.hash || window.location.hash === '#/') {
            window.location.hash = '#/tableau';
        }

        afficherApplication();
    } catch (exception) {
        dom.erreurConnexion.textContent = exception.message || 'Connexion impossible.';
        dom.erreurConnexion.hidden = false;
    } finally {
        bouton.disabled = false;
    }
});

dom.deconnexion.addEventListener('click', () => deconnecter());

surDeconnexion(() => {
    window.location.hash = '';
    vueCourante = null;
    afficherConnexion();
});

/* ------------------------------- Routage ------------------------------- */

function identifiantVueDemandee() {
    const ancre = window.location.hash.replace(/^#\/?/, '');

    return VUES[ancre] ? ancre : 'tableau';
}

async function route() {
    if (!estConnecte()) {
        afficherConnexion();

        return;
    }

    let identifiant = identifiantVueDemandee();
    const definition = NAVIGATION.find((entree) => entree.id === identifiant);

    if (!definition || !accesVue(definition)) {
        identifiant = 'tableau';
        window.location.hash = '#/tableau';
    }

    const vue = VUES[identifiant];
    vueCourante = vue;

    dom.navigation.querySelectorAll('button').forEach((bouton) => {
        bouton.classList.toggle('actif', bouton.dataset.vue === identifiant);
    });

    dom.titre.textContent = vue.titre;
    dom.sousTitre.textContent = vue.sousTitre;
    document.title = `${vue.titre} - ${metadonnees().titre}`;

    await vue.afficher(dom.vue);
}

window.addEventListener('hashchange', () => {
    if (estConnecte() && !dom.application.hidden) {
        route();
    }
});

dom.rafraichir.addEventListener('click', () => {
    if (vueCourante) {
        vueCourante.afficher(dom.vue);
    }

    verifierSante();
});

/* ------------------------------- Sante --------------------------------- */

async function verifierSante() {
    try {
        const reponse = await fetch('/health/services', { cache: 'no-store' });
        const etat = await reponse.json();
        const enLigne = etat.services.filter((service) => service.statut === 'Healthy').length;

        dom.sante.className = `pastille ${etat.statut === 'Healthy' ? 'ok' : 'degrade'}`;
        dom.sante.textContent = `● ${enLigne}/${etat.services.length} services`;
        dom.sante.title = etat.services
            .map((service) => `${service.service} : ${service.statut}`)
            .join('
');
    } catch {
        dom.sante.className = 'pastille ko';
        dom.sante.textContent = '● plateforme injoignable';
    }
}

/* -------------------------------- Demarrage ----------------------------- */

async function demarrer() {
    const configuration = await chargerConfiguration();
    document.title = configuration.titre;

    if (!estConnecte()) {
        afficherConnexion();

        return;
    }

    try {
        // Verifie que le jeton memorise est encore valide avant d'ouvrir l'application.
        await api.get('/api/auth/me');
        afficherApplication();
    } catch (exception) {
        if (exception.statut !== 401) {
            erreur(exception, 'Plateforme injoignable');
        }

        afficherConnexion();
    }
}

demarrer();
