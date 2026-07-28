"""VELO Sentinel S-B — fine-tune DistilBERT on URL classification.

GPU recommended (consumer card or Colab T4: ~1-2 h). Writes out/model/.
"""
import numpy as np
import pandas as pd
from datasets import Dataset
from sklearn.metrics import f1_score
from transformers import (
    AutoModelForSequenceClassification,
    AutoTokenizer,
    Trainer,
    TrainingArguments,
)

BASE = "distilbert-base-uncased"
MAX_LEN = 32  # hosts are short; S-A: shorter seq = faster inference

tok = AutoTokenizer.from_pretrained(BASE)


def load(split: str) -> Dataset:
    df = pd.read_csv(f"data/{split}.csv")
    ds = Dataset.from_pandas(df)
    return ds.map(
        lambda b: tok(b["url"], truncation=True, max_length=MAX_LEN, padding="max_length"),
        batched=True,
    )


def metrics(p):
    preds = np.argmax(p.predictions, axis=1)
    return {"macro_f1": f1_score(p.label_ids, preds, average="macro")}


model = AutoModelForSequenceClassification.from_pretrained(BASE, num_labels=4)

args = TrainingArguments(
    output_dir="out/checkpoints",
    num_train_epochs=3,
    per_device_train_batch_size=64,
    per_device_eval_batch_size=256,
    learning_rate=3e-5,
    warmup_ratio=0.06,
    eval_strategy="epoch",
    save_strategy="epoch",
    load_best_model_at_end=True,
    metric_for_best_model="macro_f1",
    fp16=True,
    report_to=[],
)

trainer = Trainer(
    model=model,
    args=args,
    train_dataset=load("train"),
    eval_dataset=load("val"),
    compute_metrics=metrics,
)
trainer.train()
trainer.save_model("out/model")
tok.save_pretrained("out/model")
print("saved -> out/model")
