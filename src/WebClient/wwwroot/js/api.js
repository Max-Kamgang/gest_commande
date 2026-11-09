/**
 * Client HTTP de l'interface web.
 *
 * Toutes les requetes passent par la GATEWAY, jamais par un microservice :
 * l'adresse de base est fournie par /app-config.json (vide quand la page est
 * servie par la gateway elle-meme, donc appels relatifs de meme origine).
 *
 * Responsabilites :
 *   - ajout du jeton JWT,
 *   - generation et envoi d'un Correlation-ID par requete,
 *   - lecture des erreurs au format ProblemDetails (RFC 9457),
 *   - deconnexion automatique sur 401.
 */

const CLE_JETON = 'gc.jeton';
const CLE_PROFIL = 'gc.profil';

let baseApi = '';
let configuration = { titre: 'Gestion des commandes', version: '1.0.0' };
const auditeursDeconnexion = [];

/** Erreur applicative portant le ProblemDetails renvoye par l'API. */
export class ErreurApi extends Error {
    constructor(statut, probleme, correlationId) {
        super(probleme?.detail || probleme?.title || `Erreur HTTP ${statut}`);
        this.name = 'ErreurApi';
        this.statut = statut;
        this.probleme = probleme || {};
        this.codeErreur = probleme?.errorCode || null;
        this.correlationId = probleme?.correlationId || correlationId || null;
        this.dependance = probleme?.dependency || null;
        this.erreursValidation = probleme?.errors || null;
    }

    /** Libelle lisible, incluant le detail de validation s'il existe. */
    get messageComplet() {
        if (!this.erreursValidation) {
            return this.message;
        }

        const details = Object.entries(this.erreursValidation)
            .map(([champ, messages]) => `${champ} : ${[].concat(messages).join(' ')}`)
            .join(' | ');

        return `${this.message} ${details}`.trim();
    }
}

/** Charge /app-config.json : adresse de la gateway et metadonnees. */
export async function chargerConfiguration() {
    try {
        const reponse = await fetch('/app-config.json', { cache: 'no-store' });

        if (reponse.ok) {
            const donnees = await reponse.json();
            baseApi = (donnees.apiBaseUrl || '').replace(/\/+$/, '');
            configuration = {
                titre: donnees.title || configuration.titre,
                version: donnees.version || configuration.version,
                construitAvec: donnees.builtWith || ''
            };
        }
    } catch {
        // Repli : appels relatifs de meme origine.
        baseApi = '';
    }

    return { baseApi, ...configuration };
}

/** Adresse de base effective de l'API (chaine vide = meme origine). */
export function adresseApi() {
    return baseApi;
}

/** Metadonnees de l'application. */
export function metadonnees() {
    return configuration;
}

/* ------------------------------ Session -------------------------------- */

export function jeton() {
    return localStorage.getItem(CLE_JETON);
}

export function profil() {
    const brut = localStorage.getItem(CLE_PROFIL);

    try {
        return brut ? JSON.parse(brut) : null;
    } catch {
        return null;
    }
}

export function estConnecte() {
    return Boolean(jeton()) && Boolean(profil());
}

/** Vrai si l'utilisateur connecte porte au moins un des roles demandes. */
export function aRole(...roles) {
    const miens = profil()?.roles || [];

    return roles.some((role) => miens.includes(role));
}

export function estBackOffice() {
    return aRole('Admin', 'Gestionnaire');
}

export function surDeconnexion(auditeur) {
    auditeursDeconnexion.push(auditeur);
}

export function deconnecter() {
    localStorage.removeItem(CLE_JETON);
    localStorage.removeItem(CLE_PROFIL);
    auditeursDeconnexion.forEach((auditeur) => auditeur());
}

/** Authentifie l'utilisateur et memorise le jeton et le profil. */
export async function connecter(identifiant, motDePasse) {
    const reponse = await requete('POST', '/api/auth/login', {
        corps: { userName: identifiant, password: motDePasse },
        sansJeton: true
    });

    localStorage.setItem(CLE_JETON, reponse.accessToken);
    localStorage.setItem(CLE_PROFIL, JSON.stringify(reponse.user));

    return reponse.user;
}

/* ------------------------------ Requetes ------------------------------- */

function nouvelIdentifiantCorrelation() {
    if (window.crypto?.randomUUID) {
        return window.crypto.randomUUID().replace(/-/g, '');
    }

    return `web-${Date.now()}-${Math.random().toString(16).slice(2, 10)}`;
}

/**
 * Execute une requete vers la gateway.
 * @param {string} methode GET, POST, PUT, PATCH ou DELETE.
 * @param {string} chemin Chemin relatif, par exemple /api/products.
 * @param {object} options corps, parametres, idempotencyKey, sansJeton.
 */
export async function requete(methode, chemin, options = {}) {
    const { corps, parametres, idempotencyKey, sansJeton = false } = options;

    let url = `${baseApi}${chemin}`;

    if (parametres) {
        const recherche = new URLSearchParams();

        Object.entries(parametres).forEach(([cle, valeur]) => {
            if (valeur !== undefined && valeur !== null && valeur !== '') {
                recherche.append(cle, valeur);
            }
        });

        const chaine = recherche.toString();

        if (chaine) {
            url += `?${chaine}`;
        }
    }

    const correlationId = nouvelIdentifiantCorrelation();
    const entetes = {
        Accept: 'application/json',
        'X-Correlation-ID': correlationId
    };

    if (corps !== undefined) {
        entetes['Content-Type'] = 'application/json';
    }

    if (idempotencyKey) {
        entetes['Idempotency-Key'] = idempotencyKey;
    }

    if (!sansJeton) {
        const jetonCourant = jeton();

        if (jetonCourant) {
            entetes.Authorization = `Bearer ${jetonCourant}`;
        }
    }

    let reponse;

    try {
        reponse = await fetch(url, {
            method: methode,
            headers: entetes,
            body: corps === undefined ? undefined : JSON.stringify(corps)
        });
    } catch (erreurReseau) {
        throw new ErreurApi(0, {
            title: 'Gateway injoignable',
            detail:
                "La gateway ne repond pas. Verifiez que le conteneur est demarre " +
                "et que le port 80 est autorise par le pare-feu.",
            errorCode: 'GATEWAY_UNREACHABLE'
        }, correlationId);
    }

    if (reponse.status === 204) {
        return null;
    }

    const typeContenu = reponse.headers.get('content-type') || '';
    const estJson = typeContenu.includes('json');
    const charge = estJson ? await reponse.json().catch(() => null) : await reponse.text();

    if (reponse.ok) {
        return charge;
    }

    // 401 : le jeton est absent, invalide ou expire -> retour a la connexion.
    if (reponse.status === 401 && !sansJeton) {
        deconnecter();
    }

    throw new ErreurApi(
        reponse.status,
        estJson ? charge : { title: `Erreur HTTP ${reponse.status}`, detail: String(charge).slice(0, 300) },
        reponse.headers.get('X-Correlation-ID') || correlationId
    );
}

export const api = {
    get: (chemin, parametres) => requete('GET', chemin, { parametres }),
    post: (chemin, corps, options) => requete('POST', chemin, { corps, ...options }),
    put: (chemin, corps) => requete('PUT', chemin, { corps }),
    patch: (chemin, corps) => requete('PATCH', chemin, { corps }),
    delete: (chemin, parametres) => requete('DELETE', chemin, { parametres })
};
