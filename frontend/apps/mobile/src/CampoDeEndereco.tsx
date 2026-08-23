import {
  findPostalCode,
  formatCep,
  normalizeCep,
  type Address,
  type AddressPayload,
  type ResidenceType,
} from '@congrega/api-client/addresses';
import { Dropdown } from '@congrega/ui/Dropdown';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { useCallback, useEffect, useRef, useState } from 'react';
import { ActivityIndicator, View } from 'react-native';
import { apiClient } from './api';

/** O estado editável do endereço, antes de virar payload. */
export interface EnderecoEditavel {
  cep: string;
  logradouro: string;
  bairro: string;
  localidade: string;
  estado: string;
  residenceType: ResidenceType;
  numero: string;
  andar: string;
}

export function enderecoVazio(): EnderecoEditavel {
  return {
    cep: '',
    logradouro: '',
    bairro: '',
    localidade: '',
    estado: '',
    residenceType: 'Casa',
    numero: '',
    andar: '',
  };
}

export function enderecoDe(address: Address | null | undefined): EnderecoEditavel {
  if (address === null || address === undefined) return enderecoVazio();

  return {
    cep: address.cep ?? '',
    logradouro: address.logradouro ?? '',
    bairro: address.bairro ?? '',
    localidade: address.localidade ?? '',
    estado: address.estado ?? '',
    residenceType: address.residenceType,
    numero: address.numero ?? '',
    andar: address.andar ?? '',
  };
}

/**
 * Converte para o corpo da requisição.
 *
 * Campos em branco viram `undefined` — omitidos do JSON — em vez de string
 * vazia. Um `""` chegaria ao servidor como valor informado e viraria coluna
 * vazia no banco, indistinguível de "a pessoa digitou nada de propósito".
 */
export function paraPayload(endereco: EnderecoEditavel): AddressPayload {
  const opcional = (valor: string): string | undefined => {
    const limpo = valor.trim();
    return limpo === '' ? undefined : limpo;
  };

  return {
    ...(opcional(endereco.cep) === undefined ? {} : { cep: endereco.cep.trim() }),
    ...(opcional(endereco.logradouro) === undefined ? {} : { logradouro: endereco.logradouro.trim() }),
    ...(opcional(endereco.bairro) === undefined ? {} : { bairro: endereco.bairro.trim() }),
    ...(opcional(endereco.localidade) === undefined ? {} : { localidade: endereco.localidade.trim() }),
    ...(opcional(endereco.estado) === undefined ? {} : { estado: endereco.estado.trim() }),
    residenceType: endereco.residenceType,
    ...(opcional(endereco.numero) === undefined ? {} : { numero: endereco.numero.trim() }),
    // O andar só vai quando é apartamento. O servidor o descartaria de qualquer
    // forma, mas mandar um campo que se sabe inútil embaralha o log de
    // requisição de quem for depurar.
    ...(endereco.residenceType === 'Apartamento' && opcional(endereco.andar) !== undefined
      ? { andar: endereco.andar.trim() }
      : {}),
  };
}

/**
 * Os quatro tipos de moradia, com o ícone de cada um.
 *
 * Exportado para o formulário poder desenhar o mesmo vocabulário no resumo
 * lateral sem redigitar a lista — duas cópias divergiriam na primeira mudança,
 * e foi exatamente assim que `Sitio` e `Outro` entraram: um lado de cada vez.
 */
export const TIPOS_DE_RESIDENCIA = [
  { valor: 'Casa', rotulo: 'Casa', icone: 'home' },
  { valor: 'Apartamento', rotulo: 'Apartamento', icone: 'grid' },
  { valor: 'Sitio', rotulo: 'Sítio / Chácara', icone: 'sunrise' },
  { valor: 'Outro', rotulo: 'Outro', icone: 'map-pin' },
] as const satisfies readonly {
  readonly valor: ResidenceType;
  readonly rotulo: string;
  readonly icone: keyof typeof Feather.glyphMap;
}[];

type EstadoDaBusca =
  | { readonly tipo: 'ocioso' }
  | { readonly tipo: 'buscando' }
  // Sem a origem (cache ou ViaCEP): nada na tela a mostra desde que a mensagem
  // de sucesso saiu, e campo que ninguém lê é dado morto esperando alguém
  // acreditar que ainda serve para algo. A API continua devolvendo `source`,
  // onde ele ganha o lugar dele: é o que permite verificar de fora que o banco
  // é consultado antes da ViaCEP.
  | { readonly tipo: 'encontrado' }
  | { readonly tipo: 'nao-encontrado' };

/**
 * Espera antes de consultar o CEP.
 *
 * O campo dispara a busca sozinho ao completar oito dígitos — sem botão, que é
 * o que o requisito pede. A espera existe porque "digitou o oitavo dígito" e
 * "terminou de digitar" não são a mesma coisa: quem cola um CEP e corrige o
 * último número dispararia duas consultas, e a primeira preencheria a tela com
 * o endereço errado por um instante.
 */
const ESPERA_ANTES_DE_BUSCAR = 500;

export interface CampoDeEnderecoProps {
  readonly valor: EnderecoEditavel;
  readonly onChange: (endereco: EnderecoEditavel) => void;
  /**
   * Rótulo da seção.
   *
   * `null` omite. Serve para quem já anuncia "Endereço" no título do painel que
   * envolve o campo — dois rótulos iguais, um sob o outro, fazem o leitor pensar
   * que são seções diferentes.
   */
  readonly titulo?: string | null;
}

/**
 * Endereço com busca automática de CEP.
 *
 * **Três caminhos, e o terceiro é o que importa:** o CEP está no banco (resposta
 * instantânea), está na ViaCEP (uma chamada externa, e o resultado entra no
 * cache), ou não está em lugar nenhum — e aí os campos ficam editáveis para o
 * preenchimento manual, que é o requisito explícito.
 *
 * **Os campos vindos do CEP ficam somente-leitura enquanto a busca acerta.**
 * Não é para proteger o dado — é para deixar visível de onde ele veio. Assim
 * que a busca falha, eles abrem, e a mudança de estado é a própria explicação
 * de que agora a responsabilidade é de quem digita.
 */
export function CampoDeEndereco({ valor, onChange, titulo = 'Endereço' }: CampoDeEnderecoProps) {
  const theme = useTheme();
  const [busca, setBusca] = useState<EstadoDaBusca>({ tipo: 'ocioso' });

  // Guarda o último CEP consultado para não repetir a chamada quando o
  // componente re-renderiza por outro motivo — trocar o tipo de residência, por
  // exemplo, não deve disparar uma consulta de rede.
  const ultimoConsultado = useRef<string | null>(null);

  const cepNormalizado = normalizeCep(valor.cep);

  // Ref para a versão corrente do endereço: sem isso, o efeito abaixo
  // dependeria de `valor` e re-agendaria a busca a cada tecla em qualquer
  // campo, inclusive no número da casa.
  const valorRef = useRef(valor);
  valorRef.current = valor;

  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  const aplicarResultado = useCallback(
    (resultado: { cep: string; logradouro: string; bairro: string; localidade: string; estado: string }) => {
      const atual = valorRef.current;
      onChangeRef.current({
        ...atual,
        cep: resultado.cep,
        logradouro: resultado.logradouro,
        bairro: resultado.bairro,
        localidade: resultado.localidade,
        estado: resultado.estado,
      });
    },
    [],
  );

  useEffect(() => {
    if (cepNormalizado === null) {
      ultimoConsultado.current = null;
      setBusca({ tipo: 'ocioso' });
      return;
    }

    if (ultimoConsultado.current === cepNormalizado) {
      return;
    }

    let cancelado = false;

    const timer = setTimeout(() => {
      ultimoConsultado.current = cepNormalizado;
      setBusca({ tipo: 'buscando' });

      findPostalCode(apiClient, cepNormalizado)
        .then((resultado) => {
          if (cancelado) return;
          aplicarResultado(resultado);
          setBusca({ tipo: 'encontrado' });
        })
        .catch(() => {
          // 404, rede fora, ViaCEP fora — tudo cai aqui, e tudo leva ao mesmo
          // lugar: os campos abrem para digitação. Distinguir os casos daria ao
          // usuário uma informação sobre a qual ele não pode agir.
          if (cancelado) return;
          setBusca({ tipo: 'nao-encontrado' });
        });
    }, ESPERA_ANTES_DE_BUSCAR);

    return () => {
      cancelado = true;
      clearTimeout(timer);
    };
  }, [cepNormalizado, aplicarResultado]);

  const preenchidoPelaBusca = busca.tipo === 'encontrado';
  const ehApartamento = valor.residenceType === 'Apartamento';

  return (
    <View style={{ gap: theme.space[12] }}>
      {titulo !== null && (
        <Text variant="caption" tone="muted">
          {titulo.toUpperCase()}
        </Text>
      )}

      <TextField
        label="CEP"
        placeholder="00000-000"
        defaultValue={formatCep(valor.cep)}
        syncedValue={formatCep(valor.cep)}
        onValueChange={(v) => onChange({ ...valor, cep: v })}
        transform={formatCep}
        keyboardType="number-pad"
        maxLength={9}
      />

      <MensagemDaBusca estado={busca} />

      <TextField
        label="Logradouro"
        placeholder={preenchidoPelaBusca ? '' : 'Rua, avenida, praça'}
        defaultValue={valor.logradouro}
        syncedValue={valor.logradouro}
        onValueChange={(v) => onChange({ ...valor, logradouro: v })}
        editable={!preenchidoPelaBusca}
        autoCapitalize="words"
      />

      <TextField
        label="Bairro"
        defaultValue={valor.bairro}
        syncedValue={valor.bairro}
        onValueChange={(v) => onChange({ ...valor, bairro: v })}
        editable={!preenchidoPelaBusca}
        autoCapitalize="words"
      />

      <View style={{ flexDirection: 'row', gap: theme.space[8] }}>
        <View style={{ flex: 2 }}>
          <TextField
            label="Cidade"
            defaultValue={valor.localidade}
        syncedValue={valor.localidade}
            onValueChange={(v) => onChange({ ...valor, localidade: v })}
            editable={!preenchidoPelaBusca}
            autoCapitalize="words"
          />
        </View>
        <View style={{ flex: 2 }}>
          <TextField
            label="Estado"
            defaultValue={valor.estado}
        syncedValue={valor.estado}
            onValueChange={(v) => onChange({ ...valor, estado: v })}
            editable={!preenchidoPelaBusca}
            autoCapitalize="words"
          />
        </View>
      </View>

      <Dropdown
        label="Tipo de residência"
        value={valor.residenceType}
        onChange={(v) =>
          onChange({
            ...valor,
            residenceType: (v ?? 'Casa') as ResidenceType,
            // Trocar para Casa limpa o andar em vez de escondê-lo preenchido.
            // Escondido, ele voltaria a aparecer se a pessoa trocasse de volta —
            // com um valor que ela já tinha desistido de usar.
            andar: v === 'Apartamento' ? valor.andar : '',
          })
        }
        options={TIPOS_DE_RESIDENCIA.map(({ valor, rotulo, icone }) => ({
          value: valor,
          label: rotulo,
          icon: <Feather name={icone} size={16} color={theme.colors.surfaceAccent} />,
        }))}
      />

      <View style={{ flexDirection: 'row', gap: theme.space[8] }}>
        <View style={{ flex: 1 }}>
          <TextField
            label={ehApartamento ? 'Número do apto' : 'Número'}
            placeholder={ehApartamento ? '32' : '123'}
            defaultValue={valor.numero}
        syncedValue={valor.numero}
            onValueChange={(v) => onChange({ ...valor, numero: v })}
          />
        </View>

        {/* O andar só existe em apartamento — é a mesma regra que a constraint
            do banco garante. Escondê-lo em casa evita a pergunta "que andar é a
            minha casa?", que não tem resposta. */}
        {ehApartamento && (
          <View style={{ flex: 1 }}>
            <TextField
              label="Andar"
              placeholder="3"
              defaultValue={valor.andar}
        syncedValue={valor.andar}
              onValueChange={(v) => onChange({ ...valor, andar: v })}
            />
          </View>
        )}
      </View>
    </View>
  );
}

function MensagemDaBusca({ estado }: { readonly estado: EstadoDaBusca }) {
  const theme = useTheme();

  if (estado.tipo === 'ocioso') return null;

  if (estado.tipo === 'buscando') {
    return (
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}>
        <ActivityIndicator size="small" color={theme.colors.textMuted} />
        <Text variant="captionBody" tone="muted">
          Buscando endereço…
        </Text>
      </View>
    );
  }

  if (estado.tipo === 'nao-encontrado') {
    return (
      <View
        style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}
        // `polite` e não `assertive`: é consequência do que o usuário acabou de
        // digitar, não um alerta que deva interromper o que ele está lendo.
        accessibilityLiveRegion="polite"
      >
        <Feather name="edit-3" size={14} color={theme.colors.textMuted} />
        <Text variant="captionBody" tone="muted" style={{ flexShrink: 1 }}>
          Não localizamos este CEP. Preencha o endereço abaixo.
        </Text>
      </View>
    );
  }

  // Sucesso não diz nada.
  //
  // Os três campos se preenchendo na frente da pessoa **são** a confirmação;
  // uma linha dizendo "encontrado" repete o que os olhos já viram e empurra o
  // resto do formulário para baixo. As outras duas mensagens ficam porque dizem
  // algo que os campos não dizem: "estou buscando" e "não achei, digite você".
  return null;
}

/**
 * Há algo de útil neste endereço?
 *
 * Usado pelos formulários para decidir entre **mandar** o endereço e **omiti-lo**
 * do corpo. A distinção importa: um objeto todo em branco significa "apague o
 * endereço" para o servidor, e num cadastro novo isso criaria uma linha vazia
 * no banco em vez de simplesmente não criar nada.
 *
 * O tipo de residência não conta — ele tem valor padrão e estaria preenchido
 * mesmo num formulário em que ninguém tocou.
 */
export function temEndereco(endereco: EnderecoEditavel): boolean {
  return (
    endereco.cep.trim() !== '' ||
    endereco.logradouro.trim() !== '' ||
    endereco.bairro.trim() !== '' ||
    endereco.localidade.trim() !== '' ||
    endereco.numero.trim() !== ''
  );
}
