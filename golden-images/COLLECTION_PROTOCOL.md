# Weighed-meal golden collection protocol

## 1. Collect a case

1. Choose a real meal and record whether it is restaurant-prepared or homemade. Include mixed dishes and beverages across the collection; do not infer hidden ingredients from a photograph.
2. Before plating, use a gram scale to weigh every component as it will be eaten. Record the edible, prepared amount in grams. Weigh added oil, butter, and dressing separately and enter each as its own expected component; give each such component the `hidden_fat` tag. Do not fold weighed added fat into another component.
3. Photograph the plated meal top-down and from approximately 45 degrees. For each view, take one photograph with a reference object (fork or card) and one without it. Set the case's `reference_object` to `true` for images containing the reference object and `false` for images without one. Each manifest case points at one image file; use separate case entries when evaluating distinct views.
4. Tag applicable cases with `restaurant` or `homemade`, `mixed_dish`, and `beverage`. Add `no_reference` when the selected image has no reference object. Use `hidden_fat` for a case containing a separately recorded added-fat component. Tags may be combined.
5. Pick the catalog entry that best represents each weighed component. Compute its expected kcal and macros as `weighed grams × catalog value per 100 g ÷ 100`; use the same units as the manifest (`kcal`, `protein_g`, `carbs_g`, `fat_g`). Record the catalog identity in `acceptable_identities`, formatted as `source:externalId` (for example `USDA:171477`). If the entry has no stable source ID, use `name:<canonical name>`. Never invent nutrition numbers or identities; leave unavailable values null/omitted.
6. Mark `weighed: true` only after the component was measured on the scale. The collection target is at least 50 cases, of which at least 40 have weighed ground truth.
7. Name image files `caseNN.jpg` (two-digit or greater sequential case number, e.g. `case01.jpg`) and set the manifest `image` value to the exact filename.

## 2. Complete schema-v2 example

This complete structural example uses null nutrition values and no accepted identities because those fields must be filled only from actual weighed data and verified catalog records. Replace them when collecting a real case.

```json
{
  "schema_version": 2,
  "prompt_version": "vision-stage-a-v1",
  "gate": {
    "min_recall": 0.75,
    "max_median_gram_error_percent": 45,
    "min_nutrition_backed_rate": 0.70,
    "max_false_positive_rate": 0.35,
    "min_precision": null,
    "max_median_kcal_error_percent": null,
    "max_abs_kcal_bias_percent": null,
    "min_identity_precision": null,
    "max_abstention_rate": null,
    "min_interval_coverage": null,
    "max_identity_ece": null,
    "max_kcal_cv_percent": null,
    "max_p95_latency_seconds": null,
    "max_p95_cost_usd": null
  },
  "cases": [
    {
      "image": "case01.jpg",
      "mode": "components",
      "tags": ["homemade", "mixed_dish", "hidden_fat"],
      "reference_object": true,
      "expected": [
        {
          "name": "cooked brown rice",
          "grams": 150,
          "weighed": true,
          "kcal": null,
          "protein_g": null,
          "carbs_g": null,
          "fat_g": null,
          "acceptable_identities": []
        },
        {
          "name": "olive oil",
          "grams": 5,
          "weighed": true,
          "kcal": null,
          "protein_g": null,
          "carbs_g": null,
          "fat_g": null,
          "acceptable_identities": []
        }
      ],
      "notes": "Enter nutrient values and identities only after selecting and checking the measured components' catalog records."
    }
  ]
}
```

## 3. Set and ratchet gates

Keep newly introduced thresholds `null` until an instrumented baseline is measured on a representative, reviewed set of weighed cases. Run the baseline repeatedly where appropriate; report sample size, missing-data rates, model/prompt version, p95 latency, p95 cost, and the metric distributions. Set each threshold from the observed baseline plus a documented margin that reflects measurement uncertainty and acceptable product risk: lower-bound metrics (precision, recall, identity precision, interval coverage) should be set below baseline; upper-bound metrics (errors, bias magnitude, abstention, ECE, variance, latency, and cost) should be set above baseline. Avoid a threshold that cannot be evaluated because its metric has no data.

After collecting more cases, ratchet thresholds gradually toward the product's accuracy and operational goals. Each change should be supported by the expanded measured baseline, preserve coverage of restaurant, homemade, mixed-dish, beverage, and hidden-fat cases, and be reviewed for false regressions before enforcement. Do not loosen a threshold merely to make a failing run pass; investigate the failure or explicitly document a justified baseline change.
