"""VELO Sentinel S-B — export out/model to int8 ONNX (out/velo-sentinel.onnx)."""
import os

import torch
from onnxruntime.quantization import QuantType, quantize_dynamic
from transformers import AutoModelForSequenceClassification, AutoTokenizer

MAX_LEN = 64

model = AutoModelForSequenceClassification.from_pretrained("out/model").eval()
tok = AutoTokenizer.from_pretrained("out/model")

sample = tok("https://example.com/login", truncation=True, max_length=MAX_LEN,
             padding="max_length", return_tensors="pt")

torch.onnx.export(
    model,
    (sample["input_ids"], sample["attention_mask"]),
    "out/velo-sentinel-fp32.onnx",
    input_names=["input_ids", "attention_mask"],
    output_names=["logits"],
    dynamic_axes={
        "input_ids": {0: "batch", 1: "seq"},
        "attention_mask": {0: "batch", 1: "seq"},
        "logits": {0: "batch"},
    },
    opset_version=17,
)

quantize_dynamic("out/velo-sentinel-fp32.onnx", "out/velo-sentinel.onnx",
                 weight_type=QuantType.QInt8)

for f in ("out/velo-sentinel-fp32.onnx", "out/velo-sentinel.onnx"):
    print(f"{f}: {os.path.getsize(f) / 1e6:.1f} MB")
