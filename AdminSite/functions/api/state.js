// 팀 공용 상태(체크리스트·의사결정)를 KV 한 키에 저장한다.
// ponytail: 마지막 저장이 이김(동시 수정 병합 없음). 충돌이 잦아지면 항목별 키로 나눈다.
const KEY = 'state';

export async function onRequestGet({ env }) {
  const s = await env.STATE.get(KEY);
  return new Response(s || '{}', { headers: { 'content-type': 'application/json' } });
}

export async function onRequestPut({ env, request }) {
  const text = await request.text();
  if (text.length > 200000) return new Response('too large', { status: 413 });
  let body;
  try { body = JSON.parse(text); } catch { return new Response('bad json', { status: 400 }); }
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return new Response('bad shape', { status: 400 });
  await env.STATE.put(KEY, JSON.stringify(body));
  return new Response('ok');
}
