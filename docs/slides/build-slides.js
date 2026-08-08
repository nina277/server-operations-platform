const pptxgen = require('pptxgenjs')

const INK = '0F1E2B'
const SLATE = '1B3346'
const MIST = 'EEF2F6'
const WHITE = 'FFFFFF'
const SIGNAL = 'C4471F'
const SIGNAL_BG = 'FBE9E2'
const OK = '1B6B5E'
const OK_BG = 'E2EFEC'
const MUTED = '5E7183'
const LINE = 'C9D4DE'

const F = 'Meiryo'

const pres = new pptxgen()
pres.layout = 'LAYOUT_WIDE' // 13.3 x 7.5
pres.author = '自律型サーバー運用支援システム'
pres.title = '自律型サーバー運用支援システム'

const W = 13.3
const M = 0.7 // 余白

// ---- 共通パーツ -------------------------------------------------------

function darkSlide() {
  const s = pres.addSlide()
  s.background = { color: INK }
  return s
}

function lightSlide(title, kicker) {
  const s = pres.addSlide()
  s.background = { color: WHITE }
  if (kicker) {
    s.addText(kicker, {
      x: M, y: 0.42, w: 8, h: 0.3,
      fontFace: F, fontSize: 12, color: SIGNAL, bold: true, charSpacing: 2, margin: 0,
    })
  }
  s.addText(title, {
    x: M, y: kicker ? 0.72 : 0.55, w: W - M * 2, h: 0.75,
    fontFace: F, fontSize: 30, bold: true, color: INK, margin: 0,
  })
  return s
}

// 見出しの左に置く塗り円(この資料の一貫した装飾)
function bullet(slide, x, y, label, color, bg) {
  slide.addShape(pres.ShapeType.ellipse, {
    x, y, w: 0.46, h: 0.46, fill: { color: bg }, line: { color: bg },
  })
  slide.addText(label, {
    x, y, w: 0.46, h: 0.46,
    fontFace: F, fontSize: 14, bold: true, color, align: 'center', valign: 'middle', margin: 0,
  })
}

function card(slide, x, y, w, h, fill) {
  slide.addShape(pres.ShapeType.roundRect, {
    x, y, w, h, rectRadius: 0.06,
    fill: { color: fill || MIST }, line: { color: fill ? fill : LINE, width: 1 },
  })
}

// ============ 1. 表紙 ============
{
  const s = darkSlide()
  s.addText('自律型サーバー運用支援システム', {
    x: M, y: 2.15, w: 11.5, h: 1.0,
    fontFace: F, fontSize: 40, bold: true, color: WHITE, margin: 0,
  })
  s.addText('Dockerを中心とする自宅サーバーの、検知から復旧までを安全な範囲で自動化する', {
    x: M, y: 3.25, w: 11.0, h: 0.5,
    fontFace: F, fontSize: 15, color: 'A9BCCC', margin: 0,
  })

  const stats = [
    ['811', 'サーバー試験'],
    ['278', '画面試験'],
    ['9 / 9', '成功基準'],
    ['10', '実環境で見つけた不具合'],
  ]
  stats.forEach(([n, label], i) => {
    const x = M + i * 2.85
    s.addText(n, {
      x, y: 4.55, w: 2.6, h: 0.7,
      fontFace: F, fontSize: 32, bold: true, color: i === 3 ? SIGNAL : WHITE, margin: 0,
    })
    s.addText(label, {
      x, y: 5.25, w: 2.6, h: 0.35,
      fontFace: F, fontSize: 11, color: '8DA2B5', margin: 0,
    })
  })

  s.addText('卒業制作', {
    x: M, y: 6.5, w: 6, h: 0.3, fontFace: F, fontSize: 12, color: '6C8296', margin: 0,
  })
  s.addNotes('自宅サーバーの運用を、人が見ていない時間帯でも安全に進める仕組みを作りました。実装だけでなく、実際に配置して検証した過程で分かったことを中心に話します。')
}

// ============ 2. 背景と課題 ============
{
  const s = lightSlide('個人が運用するサーバーで、同時に起きること', '背景')
  const items = [
    ['1', '見ていない時間が長い', '常時監視する人手が無い'],
    ['2', '落ちても気づかない', '気づくのは使おうとしたとき。それまで止まったまま'],
    ['3', '原因を調べる知識と時間が要る', 'ログを読み、何が起きたかを判断する必要がある'],
    ['4', '同じ障害が繰り返される', '前回どう直したかが残らない'],
  ]
  items.forEach(([n, head, sub], i) => {
    const y = 1.85 + i * 0.98
    bullet(s, M, y, n, SIGNAL, SIGNAL_BG)
    s.addText(head, {
      x: M + 0.72, y: y - 0.03, w: 6.2, h: 0.35,
      fontFace: F, fontSize: 16, bold: true, color: INK, margin: 0,
    })
    s.addText(sub, {
      x: M + 0.72, y: y + 0.3, w: 6.2, h: 0.32,
      fontFace: F, fontSize: 12, color: MUTED, margin: 0,
    })
  })

  card(s, 7.6, 1.85, 5.0, 3.5, MIST)
  s.addText('既存の監視基盤との違い', {
    x: 7.95, y: 2.1, w: 4.3, h: 0.35,
    fontFace: F, fontSize: 14, bold: true, color: INK, margin: 0,
  })
  s.addText(
    'Zabbix や Prometheus は「検知」までを解決する。\n\n' +
    'しかし検知の後 ——\n' +
    '何が起きているかを判断し、安全に直すところは人に残る。',
    { x: 7.95, y: 2.6, w: 4.3, h: 1.95, fontFace: F, fontSize: 12.5, color: SLATE, lineSpacing: 20, margin: 0 },
  )
  s.addText('本システムはここを扱う', {
    x: 7.95, y: 4.75, w: 4.3, h: 0.35,
    fontFace: F, fontSize: 13, bold: true, color: SIGNAL, margin: 0,
  })
  s.addNotes('個人や小規模でサーバーを運用すると、この4つが同時に起きます。監視基盤は検知までは解決しますが、その後の判断と復旧は人に残ります。')
}

// ============ 3. 目的と範囲 ============
{
  const s = lightSlide('検知から復旧までを、人が見ていない時間でも進める', '目的')

  s.addText('目指したこと', {
    x: M, y: 1.8, w: 5.6, h: 0.35, fontFace: F, fontSize: 15, bold: true, color: INK, margin: 0,
  })
  const goals = [
    '状態を定期的に収集する(コンテナ・HTTP・ホスト資源)',
    '障害を検知し、根拠つきで分類する',
    'ルールで判断できないものだけ外部AIへ回す',
    '安全条件をすべて満たす場合だけ自動で復旧する',
    '危険な操作は人の承認を求め、すべて監査に残す',
  ]
  s.addText(goals.map((t, i) => ({
    text: t, options: { bullet: true, breakLine: i !== goals.length - 1 },
  })), {
    x: M, y: 2.25, w: 5.6, h: 2.6,
    fontFace: F, fontSize: 13, color: SLATE, paraSpaceAfter: 10, margin: 0,
  })

  card(s, 7.1, 1.75, 5.5, 3.6, INK)
  s.addText('目指さなかったこと', {
    x: 7.45, y: 2.05, w: 4.8, h: 0.35,
    fontFace: F, fontSize: 15, bold: true, color: 'FFB59E', margin: 0,
  })
  s.addText('「全自動で何でも直す」', {
    x: 7.45, y: 2.55, w: 4.8, h: 0.5,
    fontFace: F, fontSize: 22, bold: true, color: WHITE, margin: 0,
  })
  s.addText(
    '間違った自動操作は、障害そのものより重い被害を出す。\n\n' +
    'そのため本システムの中心は、\n機能の多さではなく\n' +
    '「自動化の範囲をどう絞るか」にある。',
    { x: 7.45, y: 3.2, w: 4.8, h: 2.0, fontFace: F, fontSize: 13, color: 'C6D4E0', lineSpacing: 20, margin: 0 },
  )
  s.addNotes('全自動で何でも直すことは目指していません。間違った自動操作は障害より重い被害を出すためです。このシステムの中心は、自動化の範囲をどう絞るかという設計にあります。')
}

// ============ 4. システム構成 ============
{
  const s = lightSlide('LAN内で完結し、外部に開くのは1ポートだけ', 'システム構成')

  const box = (x, y, w, h, label, sub, fill, fg) => {
    s.addShape(pres.ShapeType.roundRect, {
      x, y, w, h, rectRadius: 0.06,
      fill: { color: fill }, line: { color: fill === WHITE ? LINE : fill, width: 1 },
    })
    s.addText(label, {
      x, y: y + (sub ? 0.1 : 0), w, h: sub ? 0.32 : h,
      fontFace: F, fontSize: 13, bold: true, color: fg, align: 'center', valign: 'middle', margin: 0,
    })
    if (sub) {
      s.addText(sub, {
        x, y: y + 0.42, w, h: 0.28,
        fontFace: F, fontSize: 9.5, color: fg === WHITE ? 'A9BCCC' : MUTED, align: 'center', margin: 0,
      })
    }
  }

  const arrow = (x, y, w, h) => s.addShape(pres.ShapeType.line, {
    x, y, w, h, line: { color: MUTED, width: 1.25, endArrowType: 'triangle' },
  })

  box(0.85, 1.85, 2.0, 0.62, 'ブラウザ', null, MIST, INK)
  arrow(1.85, 2.47, 0, 0.5)
  s.addText('HTTP 8080', {
    x: 1.95, y: 2.55, w: 1.5, h: 0.3, fontFace: F, fontSize: 9.5, color: SIGNAL, bold: true, margin: 0,
  })

  box(0.85, 2.97, 2.0, 0.78, 'nginx', '唯一の公開口', INK, WHITE)
  arrow(2.85, 3.36, 0.6, 0)
  box(3.45, 2.25, 1.9, 0.78, 'web', 'Vue 3 SPA', MIST, INK)
  box(3.45, 3.6, 1.9, 0.78, 'api', '認証・参照', MIST, INK)
  arrow(2.85, 2.64, 0.6, 0)

  box(6.0, 2.92, 1.9, 0.88, 'MySQL', '収集値・監査', MIST, INK)
  box(8.6, 2.92, 2.0, 0.88, 'worker', '収集・復旧', SLATE, WHITE)
  arrow(5.35, 3.36, 0.65, 0)
  arrow(8.6, 3.36, -0.7, 0)

  // 監視対象
  s.addShape(pres.ShapeType.roundRect, {
    x: 6.0, y: 4.55, w: 6.6, h: 1.5, rectRadius: 0.06,
    fill: { color: 'F7FAFC' }, line: { color: LINE, width: 1 },
  })
  s.addText('監視対象', {
    x: 6.2, y: 4.68, w: 2, h: 0.3, fontFace: F, fontSize: 11, bold: true, color: MUTED, margin: 0,
  })
  box(6.25, 5.05, 1.95, 0.72, 'Socket Proxy', 'Docker', WHITE, INK)
  box(8.35, 5.05, 1.9, 0.72, 'HTTP', '死活・応答', WHITE, INK)
  box(10.4, 5.05, 2.0, 0.72, 'node_exporter', 'ディスク', WHITE, INK)
  arrow(9.6, 3.8, 0, 0.75)

  card(s, 0.85, 4.55, 4.6, 1.5, SIGNAL_BG)
  s.addText('docker.sock は web/api に渡さない', {
    x: 1.1, y: 4.75, w: 4.1, h: 0.35,
    fontFace: F, fontSize: 12.5, bold: true, color: SIGNAL, margin: 0,
  })
  s.addText('Dockerへの経路は Socket Proxy のみ。\napi は収集も復旧の実行も行わない。', {
    x: 1.1, y: 5.15, w: 4.1, h: 0.75, fontFace: F, fontSize: 11.5, color: SLATE, lineSpacing: 17, margin: 0,
  })
  s.addNotes('外部に開くのは8080の1ポートだけです。Dockerへの経路はSocket Proxyに限り、webとapiにはdocker.sockを渡していません。apiは認証と参照だけを担い、収集と復旧の実行はworkerが行います。')
}

// ============ 5. セクション：設計の中心 ============
{
  const s = darkSlide()
  s.addText('設計の中心', {
    x: M, y: 2.5, w: 6, h: 0.4, fontFace: F, fontSize: 13, color: SIGNAL, bold: true, charSpacing: 3, margin: 0,
  })
  s.addText('どこまで自動でやるか、\nその線をどこに引いたか', {
    x: M, y: 3.0, w: 11, h: 1.6,
    fontFace: F, fontSize: 34, bold: true, color: WHITE, lineSpacing: 46, margin: 0,
  })
  s.addText('このシステムの本体は「監視」ではなく、自動化の範囲を絞る設計にある', {
    x: M, y: 4.85, w: 11, h: 0.4, fontFace: F, fontSize: 14, color: '93A9BC', margin: 0,
  })
  s.addNotes('ここからが設計の中心です。')
}

// ============ 6. 操作を4つに固定 ============
{
  const s = lightSlide('実行できる操作を、あらかじめ4つに固定した', '設計 1')
  s.addText('AIの応答を検証して危険なコマンドを弾くのではなく、そもそも自由記述のコマンドを通す経路を作らない', {
    x: M, y: 1.62, w: 11.9, h: 0.35, fontFace: F, fontSize: 12.5, color: MUTED, margin: 0,
  })

  const rows = [
    ['RECHECK_HTTP_HEALTH', '副作用なし。再確認するだけ', 'Low', '不要', OK, OK_BG],
    ['RESTART_ALLOWED_CONTAINER', '許可済みコンテナの再起動', 'Low', '不要', OK, OK_BG],
    ['START_ALLOWED_CONTAINER', '許可済みコンテナの開始', 'Medium', '必要', SIGNAL, SIGNAL_BG],
    ['STOP_ALLOWED_CONTAINER', '許可済みコンテナの停止', 'Medium', '必要', SIGNAL, SIGNAL_BG],
  ]
  rows.forEach(([id, desc, risk, appr, c, bg], i) => {
    const y = 2.2 + i * 0.82
    card(s, M, y, 7.6, 0.68, i % 2 === 0 ? MIST : 'F7FAFC')
    s.addText(id, {
      x: M + 0.25, y: y + 0.06, w: 4.4, h: 0.3,
      fontFace: 'Consolas', fontSize: 11.5, bold: true, color: INK, margin: 0,
    })
    s.addText(desc, {
      x: M + 0.25, y: y + 0.36, w: 5.0, h: 0.28,
      fontFace: F, fontSize: 10.5, color: MUTED, margin: 0,
    })
    s.addShape(pres.ShapeType.roundRect, {
      x: M + 5.55, y: y + 0.16, w: 0.95, h: 0.36, rectRadius: 0.08,
      fill: { color: bg }, line: { color: bg },
    })
    s.addText(risk, {
      x: M + 5.55, y: y + 0.16, w: 0.95, h: 0.36,
      fontFace: F, fontSize: 10.5, bold: true, color: c, align: 'center', valign: 'middle', margin: 0,
    })
    s.addText(appr === '必要' ? '承認 必要' : '承認 不要', {
      x: M + 6.6, y: y + 0.16, w: 1.2, h: 0.36,
      fontFace: F, fontSize: 10.5, bold: appr === '必要', color: appr === '必要' ? SIGNAL : MUTED,
      valign: 'middle', margin: 0,
    })
  })

  card(s, 8.75, 2.2, 3.85, 3.06, INK)
  s.addText('効果', {
    x: 9.05, y: 2.45, w: 3.2, h: 0.32, fontFace: F, fontSize: 13, bold: true, color: 'FFB59E', margin: 0,
  })
  s.addText('AIが何を返しても、\n任意コマンドを実行する\n経路が存在しない', {
    x: 9.05, y: 2.85, w: 3.3, h: 1.1,
    fontFace: F, fontSize: 15, bold: true, color: WHITE, lineSpacing: 24, margin: 0,
  })
  s.addText('収集したログには監視対象の側が\n任意の文字列を書き込める。\n応答は許可リストで検証し、\n一覧に無いIDは受け付けない。', {
    x: 9.05, y: 4.1, w: 3.3, h: 1.1, fontFace: F, fontSize: 11, color: 'B7C7D6', lineSpacing: 17, margin: 0,
  })
  s.addNotes('AIに診断させる以上、応答が操作につながる経路は攻撃対象になります。そこで実行できる操作を4つに固定し、この一覧にないアクションIDは受け付けないようにしました。危険なコマンドを弾くのではなく、通す経路そのものを作らない形です。')
}

// ============ 7. 6条件 ============
{
  const s = lightSlide('自動実行は、6条件をすべて満たすときだけ', '設計 2')
  const conds = [
    ['1', '自動復旧が有効', '初期値は OFF'],
    ['2', '推奨アクションがある', '対象の能力で許可されている'],
    ['3', 'Low 危険度かつ承認不要', 'Medium 以上は自動実行しない'],
    ['4', '許可リストに含まれる', '対象別。初期状態は空'],
    ['5', '回数制限を通過', 'クールダウン・上限・遮断'],
    ['6', '実行直前の再検証', '受付時の判断を信用しない'],
  ]
  conds.forEach(([n, head, sub], i) => {
    const x = M + (i % 3) * 4.05
    const y = 1.95 + Math.floor(i / 3) * 1.65
    card(s, x, y, 3.75, 1.35, MIST)
    bullet(s, x + 0.25, y + 0.22, n, INK, WHITE)
    s.addText(head, {
      x: x + 0.82, y: y + 0.24, w: 2.75, h: 0.35,
      fontFace: F, fontSize: 13, bold: true, color: INK, margin: 0,
    })
    s.addText(sub, {
      x: x + 0.82, y: y + 0.62, w: 2.75, h: 0.55,
      fontFace: F, fontSize: 10.5, color: MUTED, lineSpacing: 15, margin: 0,
    })
  })

  s.addShape(pres.ShapeType.roundRect, {
    x: M, y: 5.35, w: 11.9, h: 0.85, rectRadius: 0.06,
    fill: { color: SIGNAL_BG }, line: { color: SIGNAL_BG },
  })
  s.addText('1つでも満たさなければ実行せず、止めた理由を監査に残す', {
    x: M + 0.35, y: 5.48, w: 11.2, h: 0.32,
    fontFace: F, fontSize: 14, bold: true, color: SIGNAL, margin: 0,
  })
  s.addText('「何もしなかった」ではなく「なぜしなかったか」が残ることを重視した', {
    x: M + 0.35, y: 5.82, w: 11.2, h: 0.3,
    fontFace: F, fontSize: 11.5, color: SLATE, margin: 0,
  })
  s.addNotes('自動復旧が動くのは、この6つをすべて満たすときだけです。1つでも欠ければ実行せず、止めた理由を監査に残します。')
}

// ============ 8. 検知の流れ ============
{
  const s = lightSlide('収集からルール判定、必要な場合だけAIへ', '障害検知の流れ')

  const stage = (x, y, w, h, title, body, fill, fg, sub) => {
    s.addShape(pres.ShapeType.roundRect, {
      x, y, w, h, rectRadius: 0.06,
      fill: { color: fill }, line: { color: fill === WHITE ? LINE : fill, width: 1 },
    })
    s.addText(title, {
      x: x + 0.22, y: y + 0.14, w: w - 0.44, h: 0.32,
      fontFace: F, fontSize: 13, bold: true, color: fg, margin: 0,
    })
    s.addText(body, {
      x: x + 0.22, y: y + 0.5, w: w - 0.44, h: h - 0.65,
      fontFace: F, fontSize: 10.5, color: sub, lineSpacing: 16, margin: 0,
    })
  }

  stage(M, 1.9, 2.75, 1.85, '収集', 'Docker API\nHTTP\nnode_exporter', MIST, INK, MUTED)
  s.addShape(pres.ShapeType.line, {
    x: 3.5, y: 2.82, w: 0.5, h: 0, line: { color: MUTED, width: 1.25, endArrowType: 'triangle' },
  })
  stage(4.05, 1.9, 2.9, 1.85, 'ルール判定', '状態 / しきい値 / 正規表現', MIST, INK, MUTED)

  s.addShape(pres.ShapeType.line, {
    x: 7.0, y: 2.5, w: 0.55, h: 0, line: { color: OK, width: 1.5, endArrowType: 'triangle' },
  })
  s.addText('一致', { x: 6.99, y: 2.16, w: 0.58, h: 0.28, fontFace: F, fontSize: 9, bold: true, color: OK, align: 'center', margin: 0 })
  s.addShape(pres.ShapeType.line, {
    x: 7.0, y: 3.35, w: 0.55, h: 0, line: { color: MUTED, width: 1.5, endArrowType: 'triangle' },
  })
  s.addText('不一致', { x: 6.99, y: 3.44, w: 0.58, h: 0.28, fontFace: F, fontSize: 9, color: MUTED, align: 'center', margin: 0 })

  stage(7.6, 1.9, 2.5, 1.1, '診断', '根拠つきで分類', OK_BG, OK, SLATE)
  stage(7.6, 3.05, 2.5, 1.35, '外部AIへ', '匿名化して送信\n応答は許可リストで検証', MIST, INK, MUTED)

  s.addShape(pres.ShapeType.line, {
    x: 10.15, y: 2.82, w: 0.5, h: 0, line: { color: MUTED, width: 1.25, endArrowType: 'triangle' },
  })
  stage(10.7, 1.9, 1.9, 1.85, 'インシデント', '障害署名で\n1件に集約', INK, WHITE, 'B7C7D6')

  s.addShape(pres.ShapeType.line, {
    x: 11.65, y: 3.78, w: 0, h: 0.45, line: { color: MUTED, width: 1.25, endArrowType: 'triangle' },
  })
  stage(9.3, 4.3, 3.3, 1.05, '自動復旧の判定', '6条件をすべて確認', SIGNAL_BG, SIGNAL, SLATE)

  card(s, M, 4.3, 8.15, 1.75, MIST)
  s.addText('障害署名による集約', {
    x: M + 0.3, y: 4.5, w: 7.5, h: 0.32, fontFace: F, fontSize: 13, bold: true, color: INK, margin: 0,
  })
  s.addText(
    '同じ障害が繰り返し検知されてもインシデントは1件にまとめる。署名は「対象 + サービス + 分類 + 正規化したログ」から算出し、\n' +
    'ログ中の数値は伏せる。タイムスタンプやプロセスIDが変わるだけで別の障害として扱われるのを防ぐため。',
    { x: M + 0.3, y: 4.88, w: 7.5, h: 0.95, fontFace: F, fontSize: 11, color: SLATE, lineSpacing: 17, margin: 0 },
  )
  s.addNotes('収集した値をルールで判定し、当たらなかったものだけAIへ回します。費用と情報漏れを抑えるためです。同じ障害は障害署名で1件に集約します。')
}

// ============ 9. 検証の設計 ============
{
  const s = lightSlide('自動試験と実環境試験を分けた', '検証の設計')

  card(s, M, 1.9, 5.75, 2.5, MIST)
  s.addText('自動試験', {
    x: M + 0.35, y: 2.15, w: 4.8, h: 0.35, fontFace: F, fontSize: 16, bold: true, color: INK, margin: 0,
  })
  s.addText('保証するのは「判断の筋道が仕様どおりか」', {
    x: M + 0.35, y: 2.55, w: 5.0, h: 0.3, fontFace: F, fontSize: 11.5, color: MUTED, margin: 0,
  })
  s.addText('サーバー 811 件 / 画面 278 件', {
    x: M + 0.35, y: 2.95, w: 5.0, h: 0.45, fontFace: F, fontSize: 18, bold: true, color: SLATE, margin: 0,
  })
  s.addText('CI(GitHub Actions)で毎回実行', {
    x: M + 0.35, y: 3.5, w: 5.0, h: 0.3, fontFace: F, fontSize: 11, color: MUTED, margin: 0,
  })

  card(s, 6.85, 1.9, 5.75, 2.5, INK)
  s.addText('実環境試験', {
    x: 7.2, y: 2.15, w: 4.8, h: 0.35, fontFace: F, fontSize: 16, bold: true, color: WHITE, margin: 0,
  })
  s.addText('実際のコンテナ・ネットワークで動くか', {
    x: 7.2, y: 2.55, w: 5.0, h: 0.3, fontFace: F, fontSize: 11.5, color: '9FB4C6', margin: 0,
  })
  s.addText('検証専用VM で通し実行', {
    x: 7.2, y: 2.95, w: 5.0, h: 0.45, fontFace: F, fontSize: 18, bold: true, color: WHITE, margin: 0,
  })
  s.addText('配置 → MFA設定 → 対象登録 → シナリオ → 測定 を自動化', {
    x: 7.2, y: 3.5, w: 5.1, h: 0.3, fontFace: F, fontSize: 11, color: '9FB4C6', margin: 0,
  })

  s.addShape(pres.ShapeType.roundRect, {
    x: M, y: 4.7, w: 11.9, h: 1.5, rectRadius: 0.06,
    fill: { color: SIGNAL_BG }, line: { color: SIGNAL_BG },
  })
  s.addText('この分離は結果的に正しかった', {
    x: M + 0.35, y: 4.9, w: 11.2, h: 0.35,
    fontFace: F, fontSize: 15, bold: true, color: SIGNAL, margin: 0,
  })
  s.addText(
    '自動試験780件がすべて緑の状態で、配置すると起動しない不具合が4件あった。\n' +
    '実際にコンテナが再起動するか、マイグレーションが通るか、healthcheckが効くかは実環境でしか確かめられない。',
    { x: M + 0.35, y: 5.3, w: 11.2, h: 0.75, fontFace: F, fontSize: 12, color: SLATE, lineSpacing: 18, margin: 0 },
  )
  s.addNotes('検証は2層に分けました。自動試験は判断の筋道までしか保証しません。この分離は結果的に正しく、780件が全部緑の状態で配置すると起動しない不具合が4件ありました。')
}

// ============ 10. 成功基準 ============
{
  const s = lightSlide('検証専用VMでの通し実行で、9項目すべてを達成', '成果')

  const criteria = [
    ['1', '障害が検知されインシデントが作られる', '判定10件中10件'],
    ['2', '検知から通知までが5分以内', '中央値 0秒 / 最大 1.2秒'],
    ['3', '診断に必ず根拠が付く', '根拠が空 0件'],
    ['4', '危険度の高い操作が自動実行されない', '低危険度のみ'],
    ['5', '許可外のコンテナが操作されない', '拒否を監査で確認'],
    ['6', 'プロンプト注入が実行に結びつかない', '取り込み1件 / 実行0件'],
    ['7', '秘密情報が画面・API応答・ログに出ない', '応答を実測'],
    ['8', '操作がすべて監査される', '監査 49件'],
    ['9', '権限のない利用者が操作できない', 'HTTP経由で403を確認'],
  ]
  criteria.forEach(([n, name, evidence], i) => {
    const x = M + (i % 2) * 6.1
    const y = 1.85 + Math.floor(i / 2) * 0.66
    s.addShape(pres.ShapeType.ellipse, {
      x, y: y + 0.06, w: 0.34, h: 0.34, fill: { color: OK_BG }, line: { color: OK_BG },
    })
    s.addText(n, {
      x, y: y + 0.06, w: 0.34, h: 0.34,
      fontFace: F, fontSize: 11, bold: true, color: OK, align: 'center', valign: 'middle', margin: 0,
    })
    s.addText(name, {
      x: x + 0.45, y: y + 0.04, w: 3.5, h: 0.4,
      fontFace: F, fontSize: 11.5, color: INK, valign: 'middle', margin: 0,
    })
    s.addText(evidence, {
      x: x + 4.0, y: y + 0.04, w: 1.9, h: 0.4,
      fontFace: F, fontSize: 10, color: MUTED, valign: 'middle', margin: 0,
    })
  })

  s.addText('9 / 9', {
    x: 9.0, y: 5.05, w: 1.6, h: 0.7, fontFace: F, fontSize: 34, bold: true, color: OK, margin: 0,
  })
  s.addText('達成', {
    x: 10.55, y: 5.28, w: 1.5, h: 0.4, fontFace: F, fontSize: 14, bold: true, color: SLATE, valign: 'middle', margin: 0,
  })
  s.addText('SC-06・SC-07 の数値の突き合わせも判定に含めた\n(以前は「要目視」で、見なかったときに黙って通る状態だった)', {
    x: M, y: 5.1, w: 7.5, h: 0.7, fontFace: F, fontSize: 11, color: MUTED, lineSpacing: 17, margin: 0,
  })
  s.addNotes('検証専用VMでの通し実行で、成功基準9項目すべてを達成しました。数値の突き合わせも目視ではなく判定に落としています。')
}

// ============ 11. セクション：動かして分かったこと ============
{
  const s = darkSlide()
  s.addText('この研究で最も価値のある部分', {
    x: M, y: 2.4, w: 8, h: 0.4, fontFace: F, fontSize: 13, color: SIGNAL, bold: true, charSpacing: 3, margin: 0,
  })
  s.addText('動かして初めて\n分かったこと', {
    x: M, y: 2.9, w: 11, h: 1.7,
    fontFace: F, fontSize: 36, bold: true, color: WHITE, lineSpacing: 48, margin: 0,
  })
  s.addText('自動試験がすべて緑でも見つからず、実際に配置して初めて出た不具合', {
    x: M, y: 4.9, w: 11, h: 0.4, fontFace: F, fontSize: 14, color: '93A9BC', margin: 0,
  })
  s.addNotes('ここからが、この研究で最も価値があると考えている部分です。')
}

// ============ 12. 配置できない4件 ============
{
  const s = lightSlide('自動試験780件が緑でも、配置すると起動しなかった', '発見 1')
  s.addText('4件すべて「起動しない」種類で、1件でも残っていればシステムは動かない', {
    x: M, y: 1.62, w: 11.9, h: 0.35, fontFace: F, fontSize: 12.5, color: MUTED, margin: 0,
  })

  const items = [
    ['行サイズ超過', 'utf8mb4は1文字4バイト。MySQLの行サイズ上限を超え、CREATE TABLE 自体が失敗する'],
    ['ランタイム画像が違う', 'Data Protection が ASP.NET Core のフレームワーク参照を要求する'],
    ['Hangfireのキュー指定', 'MySqlStorage が非対応の書き方で、起動時に例外になる'],
    ['鍵が書けない', 'ボリュームがroot所有で作られ、非rootで動くアプリが書き込めない'],
  ]
  items.forEach(([head, body], i) => {
    const x = M + (i % 2) * 6.1
    const y = 2.2 + Math.floor(i / 2) * 1.6
    card(s, x, y, 5.75, 1.35, MIST)
    s.addText(head, {
      x: x + 0.3, y: y + 0.18, w: 5.15, h: 0.32,
      fontFace: F, fontSize: 14, bold: true, color: INK, margin: 0,
    })
    s.addText(body, {
      x: x + 0.3, y: y + 0.56, w: 5.15, h: 0.65,
      fontFace: F, fontSize: 11, color: MUTED, lineSpacing: 16, margin: 0,
    })
  })

  s.addShape(pres.ShapeType.roundRect, {
    x: M, y: 5.5, w: 11.9, h: 0.72, rectRadius: 0.06,
    fill: { color: INK }, line: { color: INK },
  })
  s.addText('行サイズ・画像の選択・ライブラリの制約・ファイルの所有者 —— 自動試験は1つも見ていなかった', {
    x: M + 0.35, y: 5.5, w: 11.2, h: 0.72,
    fontFace: F, fontSize: 13, bold: true, color: WHITE, valign: 'middle', margin: 0,
  })
  s.addNotes('配置して初めて出た4件です。いずれも起動しない種類で、1件でも残ればシステムは動きません。自動試験はこれらを1つも見ていませんでした。')
}

// ============ 13. ログを読んでいなかった ============
{
  const s = lightSlide('稼働中コンテナのログを、一度も読んでいなかった', '発見 2')

  card(s, M, 1.9, 5.75, 2.75, SIGNAL_BG)
  s.addText('修正前', {
    x: M + 0.3, y: 2.1, w: 3, h: 0.3, fontFace: F, fontSize: 12, bold: true, color: SIGNAL, margin: 0,
  })
  s.addText('ログ抜粋は停止したコンテナからしか\n取得していなかった', {
    x: M + 0.3, y: 2.48, w: 5.1, h: 0.65,
    fontFace: F, fontSize: 13.5, bold: true, color: INK, lineSpacing: 21, margin: 0,
  })
  s.addText(
    '動き続けたままエラーを出すコンテナのログは永久に読まれない。\n\n' +
    'ディスク逼迫のシナリオ(tmpfsを満たす。コンテナは動き続ける)は構造的に検知不能だった。',
    { x: M + 0.3, y: 3.25, w: 5.1, h: 1.25, fontFace: F, fontSize: 11.5, color: SLATE, lineSpacing: 17, margin: 0 },
  )

  card(s, 6.85, 1.9, 5.75, 2.75, OK_BG)
  s.addText('修正後', {
    x: 7.15, y: 2.1, w: 3, h: 0.3, fontFace: F, fontSize: 12, bold: true, color: OK, margin: 0,
  })
  s.addText('稼働中コンテナのログも走査し、\n検知に使う', {
    x: 7.15, y: 2.48, w: 5.1, h: 0.65,
    fontFace: F, fontSize: 13.5, bold: true, color: INK, lineSpacing: 21, margin: 0,
  })
  s.addText(
    '署名には「一致した部分だけ」を使う。ログ末尾をそのまま使うと行が流れるたびに署名が変わり、収集のたびに新しいインシデントが積み上がる。\n\n' +
    '実測: 12回の検知が1件に集約された。',
    { x: 7.15, y: 3.25, w: 5.1, h: 1.25, fontFace: F, fontSize: 11.5, color: SLATE, lineSpacing: 17, margin: 0 },
  )

  s.addShape(pres.ShapeType.roundRect, {
    x: M, y: 4.95, w: 11.9, h: 1.25, rectRadius: 0.06,
    fill: { color: MIST }, line: { color: LINE, width: 1 },
  })
  s.addText('ここから自動復旧は呼ばない', {
    x: M + 0.35, y: 5.12, w: 11.2, h: 0.32,
    fontFace: F, fontSize: 13.5, bold: true, color: INK, margin: 0,
  })
  s.addText(
    'ログの中身は監視対象の側が自由に書ける。これを自動実行の引き金にすると、ログに書き込める者が稼働中のコンテナを再起動させられる。\n' +
    '停止したコンテナを戻すのと、動いているものを止めるのは危険度が違う。',
    { x: M + 0.35, y: 5.5, w: 11.2, h: 0.65, fontFace: F, fontSize: 11.5, color: SLATE, lineSpacing: 17, margin: 0 },
  )
  s.addNotes('ログ抜粋は停止したコンテナからしか取っておらず、動き続けたままエラーを出すコンテナは永久に読まれませんでした。修正しましたが、ここから自動復旧は呼びません。ログは監視対象が自由に書ける値だからです。')
}

// ============ 14. 検証が空振り ============
{
  const s = lightSlide('検証そのものが空振りしていても、気づけなかった', '発見 3')

  s.addShape(pres.ShapeType.roundRect, {
    x: M, y: 1.85, w: 11.9, h: 0.95, rectRadius: 0.06,
    fill: { color: SIGNAL }, line: { color: SIGNAL },
  })
  s.addText('成功基準#6「プロンプト注入が実行に結びつかない」が、一度も注入を試さずに「達成」と出ていた', {
    x: M + 0.4, y: 1.85, w: 11.1, h: 0.95,
    fontFace: F, fontSize: 15, bold: true, color: WHITE, valign: 'middle', margin: 0,
  })

  s.addText('原因は2つ重なっていた', {
    x: M, y: 3.05, w: 6, h: 0.3, fontFace: F, fontSize: 13, bold: true, color: INK, margin: 0,
  })
  const causes = [
    ['1', 'シナリオの出力がシステムへ届いていなかった',
      'docker compose exec の出力は exec セッションにしか出ず、収集が読むログストリームに入らない'],
    ['2', '注入文はインシデントを作らない',
      'どのルールにも当たらないため保存も診断もされず、AIへ渡る経路を通っていなかった'],
  ]
  causes.forEach(([n, head, body], i) => {
    const y = 3.5 + i * 1.05
    bullet(s, M, y, n, SIGNAL, SIGNAL_BG)
    s.addText(head, {
      x: M + 0.72, y: y - 0.02, w: 6.5, h: 0.32,
      fontFace: F, fontSize: 13, bold: true, color: INK, margin: 0,
    })
    s.addText(body, {
      x: M + 0.72, y: y + 0.32, w: 6.5, h: 0.6,
      fontFace: F, fontSize: 11, color: MUTED, lineSpacing: 16, margin: 0,
    })
  })

  card(s, 8.1, 3.05, 4.5, 2.55, INK)
  s.addText('なぜ気づけなかったか', {
    x: 8.4, y: 3.28, w: 3.9, h: 0.32, fontFace: F, fontSize: 13, bold: true, color: 'FFB59E', margin: 0,
  })
  s.addText(
    '判定が別の値(自動実行の件数)から導かれていた。\n\n' +
    '「0件を達成と書かない」保護は用意してあったが、この基準だけ分母が違ったため効かなかった。',
    { x: 8.4, y: 3.7, w: 3.9, h: 1.7, fontFace: F, fontSize: 11.5, color: 'C6D4E0', lineSpacing: 18, margin: 0 },
  )
  s.addNotes('これが一番重い発見です。基準6は一度も注入を試さずに達成と出ていました。守りを1つ入れただけでは足りず、どの判定がどの材料に依っているかを個別に確かめる必要がありました。')
}

// ============ 15. 設計上の限界 ============
{
  const s = lightSlide('自分自身のハングからは、自力で復帰できない', '設計上の限界')

  s.addText('ホストから api のプロセスを停止させ、ハングを再現した', {
    x: M, y: 1.62, w: 11.9, h: 0.35, fontFace: F, fontSize: 12.5, color: MUTED, margin: 0,
  })

  const steps = [
    ['+91秒', 'healthy → unhealthy', '設定どおり 30秒 × 3回で検知', OK, OK_BG],
    ['+181秒', 'unhealthy のまま', 'status=running、再起動されない', SIGNAL, SIGNAL_BG],
    ['復帰後', 'healthy へ戻る', 'プロセスを再開すると回復', MUTED, MIST],
  ]
  steps.forEach(([t, head, sub, c, bg], i) => {
    const x = M + i * 4.05
    card(s, x, 2.2, 3.75, 1.75, bg)
    s.addText(t, {
      x: x + 0.3, y: 2.42, w: 3.15, h: 0.42,
      fontFace: F, fontSize: 20, bold: true, color: c, margin: 0,
    })
    s.addText(head, {
      x: x + 0.3, y: 2.92, w: 3.15, h: 0.32,
      fontFace: F, fontSize: 13, bold: true, color: INK, margin: 0,
    })
    s.addText(sub, {
      x: x + 0.3, y: 3.28, w: 3.15, h: 0.5,
      fontFace: F, fontSize: 10.5, color: MUTED, lineSpacing: 15, margin: 0,
    })
  })

  s.addShape(pres.ShapeType.roundRect, {
    x: M, y: 4.35, w: 11.9, h: 1.85, rectRadius: 0.06,
    fill: { color: INK }, line: { color: INK },
  })
  s.addText('Docker Compose は unhealthy なコンテナを再起動しない', {
    x: M + 0.4, y: 4.6, w: 11.1, h: 0.4,
    fontFace: F, fontSize: 17, bold: true, color: WHITE, margin: 0,
  })
  s.addText(
    'restart: unless-stopped はプロセスが終了したときにしか働かない。healthcheck は検知するが、復旧はしない。\n\n' +
    '本システムは他のサービスのハングは検知・復旧できるが、自分自身のハングからは自力で復帰できない。解決には外部の監視役が要る。',
    { x: M + 0.4, y: 5.08, w: 11.1, h: 1.0, fontFace: F, fontSize: 12, color: 'C6D4E0', lineSpacing: 19, margin: 0 },
  )
  s.addNotes('healthcheckは検知しますが、Docker Composeはunhealthyなコンテナを再起動しません。つまり他のサービスのハングは直せても、自分自身のハングからは自力で戻れません。自律運用システムとしての構造的な限界です。')
}

// ============ 16. 結論 ============
{
  const s = darkSlide()
  s.addText('結論', {
    x: M, y: 0.85, w: 6, h: 0.4, fontFace: F, fontSize: 13, color: SIGNAL, bold: true, charSpacing: 3, margin: 0,
  })

  s.addText('自動試験は「判断の筋道」しか保証しない', {
    x: M, y: 1.4, w: 11.5, h: 0.7,
    fontFace: F, fontSize: 30, bold: true, color: WHITE, margin: 0,
  })

  s.addShape(pres.ShapeType.roundRect, {
    x: M, y: 2.4, w: 5.75, h: 2.05, rectRadius: 0.06,
    fill: { color: SLATE }, line: { color: SLATE },
  })
  s.addText('得られたもの', {
    x: M + 0.35, y: 2.62, w: 5.05, h: 0.32, fontFace: F, fontSize: 13, bold: true, color: '9FE0D2', margin: 0,
  })
  s.addText(
    '検知から復旧までを人が見ていない時間でも進める仕組みを作り、成功基準9項目すべてを達成した。\n\n' +
    '実行できる操作を4つに固定し、自動実行を6条件の通過に限ることで、任意コマンドを実行する経路が存在しない形にした。',
    { x: M + 0.35, y: 3.02, w: 5.05, h: 1.3, fontFace: F, fontSize: 11.5, color: 'C6D4E0', lineSpacing: 18, margin: 0 },
  )

  s.addShape(pres.ShapeType.roundRect, {
    x: 6.85, y: 2.4, w: 5.75, h: 2.05, rectRadius: 0.06,
    fill: { color: SLATE }, line: { color: SLATE },
  })
  s.addText('分かったこと', {
    x: 7.2, y: 2.62, w: 5.05, h: 0.32, fontFace: F, fontSize: 13, bold: true, color: 'FFB59E', margin: 0,
  })
  s.addText(
    '780件が緑でも、行サイズ・画像の選択・ライブラリの制約・所有者・名前解決は1つも見ていなかった。\n\n' +
    'さらに、検証そのものが空振りしていても気づけなかった。検証を自動化したこと自体が、検証の空白を見えなくしていた。',
    { x: 7.2, y: 3.02, w: 5.05, h: 1.3, fontFace: F, fontSize: 11.5, color: 'C6D4E0', lineSpacing: 18, margin: 0 },
  )

  s.addShape(pres.ShapeType.roundRect, {
    x: M, y: 4.75, w: 11.9, h: 1.15, rectRadius: 0.06,
    fill: { color: SIGNAL }, line: { color: SIGNAL },
  })
  s.addText('「確かめたつもり」をどう潰すかが、機能を増やすことより重要だった', {
    x: M + 0.4, y: 4.75, w: 11.1, h: 1.15,
    fontFace: F, fontSize: 18, bold: true, color: WHITE, valign: 'middle', margin: 0,
  })
  s.addNotes('自動試験は判断の筋道しか保証しません。さらに、検証そのものが空振りしていても気づけませんでした。自律的に運用を支援するシステムを作るうえで、確かめたつもりをどう潰すかが機能を増やすことより重要でした。')
}

// ============ 付録1. 技術構成 ============
{
  const s = lightSlide('技術構成と実装規模', '付録')

  const tech = [
    ['Backend', 'C# / ASP.NET Core (.NET 10)', '静的型で権限や危険度の取り違えを落とす'],
    ['ORM', 'EF Core 9.0', '9.0で固定。10.xは移行の生成物が変わる'],
    ['ジョブ', 'Hangfire', '対象ごとの定期実行とキュー分離'],
    ['Frontend', 'Vue 3 / TypeScript / Vite', '画面数に対して構成が軽い'],
    ['DB', 'MySQL 8.4', '日時は datetime(6) で統一'],
  ]
  tech.forEach(([k, v, why], i) => {
    const y = 1.9 + i * 0.72
    s.addText(k, {
      x: M, y, w: 1.5, h: 0.3, fontFace: F, fontSize: 11.5, bold: true, color: MUTED, margin: 0,
    })
    s.addText(v, {
      x: M + 1.55, y: y - 0.02, w: 3.4, h: 0.32, fontFace: F, fontSize: 12.5, bold: true, color: INK, margin: 0,
    })
    s.addText(why, {
      x: M + 1.55, y: y + 0.3, w: 5.3, h: 0.3, fontFace: F, fontSize: 10.5, color: MUTED, margin: 0,
    })
  })

  const scale = [
    ['約29,600行', 'バックエンド (C#)'],
    ['約15,700行', 'フロントエンド (Vue/TS)'],
    ['18', '画面'],
    ['11', 'DBマイグレーション'],
  ]
  scale.forEach(([n, label], i) => {
    const x = 8.1 + (i % 2) * 2.35
    const y = 1.95 + Math.floor(i / 2) * 1.5
    card(s, x, y, 2.2, 1.25, MIST)
    s.addText(n, {
      x: x + 0.2, y: y + 0.25, w: 1.85, h: 0.45,
      fontFace: F, fontSize: 15, bold: true, color: INK, margin: 0,
    })
    s.addText(label, {
      x: x + 0.2, y: y + 0.74, w: 1.85, h: 0.35,
      fontFace: F, fontSize: 10, color: MUTED, margin: 0,
    })
  })
  s.addNotes('付録です。質疑で技術選定を聞かれた場合に使います。')
}

// ============ 付録2. 見つけた不具合一覧 ============
{
  const s = lightSlide('実環境で見つけた不具合の一覧', '付録')

  const bugs = [
    ['配置を妨げる不具合', '4件', '行サイズ超過 / ランタイム画像 / Hangfireのキュー / 鍵の書き込み'],
    ['稼働中コンテナのログ未読', '1件', 'ログ検知が停止後にしか当たらず、SC-04が構造的に検知不能'],
    ['nginx が更新のたびに502', '1件', '名前解決を起動時に固定。手で再起動するまで直らない'],
    ['healthcheck が常に失敗', '3件', 'localhost が IPv6 になる / Worker の心拍が未登録'],
    ['基準#6 が未試験で達成表示', '1件', '判定の分母が別の値だった'],
    ['配色が WCAG 2.2 AA 未達', '6件', '暗い配色の主ボタンが 2.34:1(必要 4.5:1)'],
  ]
  bugs.forEach(([name, n, detail], i) => {
    const y = 1.9 + i * 0.72
    s.addShape(pres.ShapeType.roundRect, {
      x: M, y, w: 11.9, h: 0.62, rectRadius: 0.05,
      fill: { color: i % 2 === 0 ? MIST : 'F7FAFC' }, line: { color: i % 2 === 0 ? MIST : 'F7FAFC' },
    })
    s.addText(name, {
      x: M + 0.3, y, w: 3.5, h: 0.62,
      fontFace: F, fontSize: 12, bold: true, color: INK, valign: 'middle', margin: 0,
    })
    s.addShape(pres.ShapeType.roundRect, {
      x: M + 3.85, y: y + 0.14, w: 0.72, h: 0.34, rectRadius: 0.07,
      fill: { color: SIGNAL_BG }, line: { color: SIGNAL_BG },
    })
    s.addText(n, {
      x: M + 3.85, y: y + 0.14, w: 0.72, h: 0.34,
      fontFace: F, fontSize: 10.5, bold: true, color: SIGNAL, align: 'center', valign: 'middle', margin: 0,
    })
    s.addText(detail, {
      x: M + 4.75, y, w: 6.9, h: 0.62,
      fontFace: F, fontSize: 10.5, color: MUTED, valign: 'middle', margin: 0,
    })
  })

  s.addText('いずれも自動試験がすべて緑の状態で見つかったもの', {
    x: M, y: 6.3, w: 11.9, h: 0.3, fontFace: F, fontSize: 11.5, bold: true, color: SIGNAL, margin: 0,
  })
  s.addNotes('付録です。質疑で不具合の詳細を聞かれた場合に使います。')
}

pres.writeFile({ fileName: process.argv[2] || 'presentation.pptx' })
  .then((f) => console.log('created:', f))
