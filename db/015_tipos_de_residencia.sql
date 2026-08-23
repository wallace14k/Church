-- Tipo de residência ganha Sítio/Chácara e Outro.
--
-- O modelo nasceu com dois valores porque o requisito original falava de "casa
-- ou apto". Uma igreja no interior cadastra sítio e chácara o tempo todo, e sem
-- o valor essas pessoas caíam em "Casa" — que não é falso o bastante para
-- alguém notar, e não é verdadeiro o bastante para servir a nada.

ALTER TABLE addresses DROP CONSTRAINT ck_addresses_residence_type;

ALTER TABLE addresses
    ADD CONSTRAINT ck_addresses_residence_type CHECK (residence_type BETWEEN 1 AND 4);

-- A regra do andar precisa ser reescrita, e é o ponto sutil desta migration.
--
-- A versão anterior era `residence_type <> 1 OR andar IS NULL`: proibia andar
-- em casa, o que cobria tudo enquanto só existiam dois valores. Com sítio e
-- "outro" no conjunto, essa mesma expressão passaria a PERMITIR andar nos dois
-- valores novos — sítio com "3º andar" entraria sem ninguém ver.
--
-- Invertida, a regra diz o que sempre quis dizer: o andar pertence ao
-- apartamento, e a mais nada. Vale para qualquer valor que venha depois.
ALTER TABLE addresses DROP CONSTRAINT ck_addresses_andar;

ALTER TABLE addresses
    ADD CONSTRAINT ck_addresses_andar CHECK (
        residence_type = 2 OR andar IS NULL
    );
