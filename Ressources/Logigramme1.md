# Logigramme 1

## Nœuds

1 - "Génération de module réussie"
Cet événement est dérivé de l'événement ModuleStatus.
Si un module qui n'avait pas été vu auparavant apparaît, c'est que la génération du module a réussi.
Le module doit avoir un "id" — peut-être un nom différent.

2 - "La porte est fermée et appuie sur un bouton de création de module 6V, 12V moto ou voiture ou bouton de validation dans les 2 secondes suivantes ?"
La porte fermée correspond au premier boolean à false dans "Porte? ouverture".
Ensuite, c'est soit :
l'utilisateur change de sélection de module à créer en cliquant sur un des 4 boutons de création de module (4 strings différentes) — voir M?-SelectModule,
soit
il clique sur le bouton de validation — voir M?-Validation.

3 - "Porte fermée et sortie de la zone du générateur OU main à côté de la porte ET sortie de la zone de générateur"
L'événement se déclenche si :

La porte fermée correspond au premier boolean à false dans "Porte? ouverture"
et en plus l'utilisateur sort de la zone du générateur Area? décrit ici :

  - **`Area1`**, **`Area2`** (zone entry/exit – see `TriggerEvent`)
    - `id` (`int`):
      - `-1`: a **player** entered/left the zone without a specific module.
      - `>= 0`: ID of the **module** entering/leaving the zone (from `Module.id.Value`).
    - `state` (`bool`):
      - `true`: entity **entered** the zone.
      - `false`: entity **exited** the zone.
    - `info` (`string`): name of the logical zone, exactly `myEvent.ToString()` from `TriggerEvent`, e.g. `"GeneratorArea"`, `"TVArea"`, `"LevierArea"`, `"CarpetArea"`.

ou bien

La main est à côté de la porte du générateur (voir `?-LeftWrist`, `?-RightWrist`); on pourra récupérer les bounds des objets via un événement et il faudra être à moins de 1mètre de la porte.
et en plus l'utilisateur sort de la zone du générateur Area?

4 - "Regard sur indicateur visuel porte fermée / 150 à 250 ms ET/OU regard sur la porte ? / 150 à 250 ms dans un laps de temps de 3 s"

Là, il faut regarder l'événement Gaze? ou GazeEvent et voir si on regarde la porte du générateur.
À voir ce qu'il est possible de faire ici. Si jamais ce n'est pas possible / ne fonctionne pas, on pourra récupérer les bounds des objets via un événement et faire le calcul à la maison (collision trait vs boîte à recoder).

5 - "Porte fermée"
La porte fermée correspond au premier boolean à false dans "Porte? ouverture".

6 - "Porte fermée"
La porte fermée correspond au premier boolean à false dans "Porte? ouverture".

7 - "Action sur bouton validé ×3 ou séquence module + validé similaire ×3"
L'événement se déclenche soit si
l'utilisateur appuie trois fois sur le bouton de validation — voir M?-Validation,
soit
l'utilisateur effectue trois fois cette action : un des 4 boutons de création de module (4 strings différentes) — voir M?-SelectModule + enchaîne sur le bouton de validation — voir M?-Validation.

8 - "Appui sur différents boutons du générateur"
L'événement se déclenche s'il appuie sur un **autre** des 4 boutons de création de module (4 strings différentes) — voir M?-SelectModule puis sur le bouton de validation — voir M?-Validation.

9 - Fermeture de la porte

## Résultats

A - Alpha
B - Beta
C - Gamma
D - N/A
E - Apprentissage

## Liens

1-2-> événement précédent déclenché
2-1-> événement précédent déclenché
2-3-> événement précédent non déclenché
3-1-> événement précédent non déclenché
3-C-> événement précédent déclenché
1-4-> événement précédent non déclenché
4-5-> événement précédent non déclenché
4-6-> événement précédent déclenché
5-D-> événement précédent déclenché
6-E-> événement précédent déclenché
5-7-> événement précédent non déclenché
6-7-> événement précédent non déclenché
7-A-> événement précédent déclenché
7-8-> événement précédent non déclenché
8-B-> événement précédent déclenché
8-9-> événement précédent non déclenché
9-D-> événement précédent déclenché
9-4-> événement précédent non déclenché
