import json
import os
import sys


def configure_stdio():
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")


def emit(payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def load_translator(model_dir):
    import ctranslate2
    import sentencepiece as spm

    sp = spm.SentencePieceProcessor()
    sp_path = os.path.join(model_dir, "sentencepiece.bpe.model")
    if not os.path.isfile(sp_path):
        raise FileNotFoundError(f"Missing sentencepiece.bpe.model in {model_dir}")
    if not sp.load(sp_path):
        raise RuntimeError("Could not load SentencePiece")

    translator = ctranslate2.Translator(model_dir, device="cpu", compute_type="int8")
    return translator, sp


def translate(translator, sp, text, src, tgt):
    tokens = sp.encode(text, out_type=str)
    source = [src] + tokens + ["</s>"]
    n = len(tokens)
    short = n <= 18
    max_len = min(512, 28 if short else max(64, n * 2 + 24))
    results = translator.translate_batch(
        [source],
        target_prefix=[[tgt]],
        beam_size=2,
        repetition_penalty=1.35 if short else 1.2,
        no_repeat_ngram_size=3,
        max_decoding_length=max_len,
        max_input_length=512,
    )
    out_tokens = list(results[0].hypotheses[0])
    if out_tokens and out_tokens[0] == tgt:
        out_tokens = out_tokens[1:]
    if out_tokens and out_tokens[-1] == "</s>":
        out_tokens = out_tokens[:-1]
    return sp.decode(out_tokens)


def main():
    configure_stdio()
    if len(sys.argv) < 2:
        emit({"ok": False, "event": "fatal", "error": "Missing NLLB model folder"})
        return 2

    model_dir = sys.argv[1]
    try:
        translator, sp = load_translator(model_dir)
    except Exception as exc:
        emit({"ok": False, "event": "fatal", "error": str(exc)})
        return 1

    emit({"ok": True, "event": "ready"})
    for raw in sys.stdin:
        line = raw.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except Exception as exc:
            emit({"ok": False, "error": f"Invalid JSON: {exc}"})
            continue

        cmd = req.get("cmd", "translate")
        req_id = req.get("id")
        if cmd == "ping":
            emit({"ok": True, "id": req_id, "event": "pong"})
            continue
        if cmd == "exit":
            emit({"ok": True, "id": req_id, "event": "bye"})
            break

        try:
            text = translate(translator, sp, req.get("text", ""), req.get("src", "eng_Latn"), req.get("tgt", "ita_Latn"))
            emit({"ok": True, "id": req_id, "text": text})
        except Exception as exc:
            emit({"ok": False, "id": req_id, "error": str(exc)})
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        emit({"ok": False, "event": "fatal", "error": str(exc)})
        raise SystemExit(1)
