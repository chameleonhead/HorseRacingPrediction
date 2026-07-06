# Lightsail Deployment

最安構成を優先した、AWS Lightsail 1 台構成のデプロイ手順です。初回セットアップは AWS CloudShell で上から順に実行する前提で書いています。

## 構成

- インフラ: Terraform で Lightsail Instance、Static IP、公開ポートを作成
- アプリ: Docker 化した API を public な GHCR イメージとして GitHub Actions から配備
- リバースプロキシ: Caddy
- データベース: SQLite を同居配置し、`/opt/horse-racing-prediction/app/data/eventstore.db` を永続化

この構成は単一ノード前提です。SQLite を使うため、複数台構成や自動スケールは想定していません。

## 関連ファイル

- `infra/lightsail`: Lightsail 用 Terraform
- `deploy`: Docker Compose と Caddy 設定
- `.github/workflows/infra-deploy.yml`: インフラ用 GitHub Actions
- `.github/workflows/app-deploy.yml`: アプリ用 GitHub Actions

## 0. CloudShell の準備

CloudShell では、AWS アカウント管理権限を持つセッションで作業します。

最初にコマンドの有無を確認します。

```bash
aws --version
gh --version
```

`gh` が未導入なら、CloudShell では公式 RPM repository を追加してインストールします。

```bash
sudo dnf config-manager --add-repo https://cli.github.com/packages/rpm/gh-cli.repo
sudo dnf install -y gh
```

GitHub CLI にログインします。

```bash
gh auth login -s repo
gh auth status
```

作業用の環境変数を設定します。

```bash
export AWS_REGION=us-east-1
export AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
export GITHUB_OWNER=chameleonhead
export GITHUB_REPO=HorseRacingPrediction
export GITHUB_BRANCH=main
export GITHUB_ENVIRONMENT=production
export TF_STATE_BUCKET=horse-racing-prediction-terraform-state
export TF_STATE_KEY=lightsail/production/terraform.tfstate
export TF_STATE_REGION=$AWS_REGION
export AWS_ROLE_NAME=GitHubActionsHorseRacingPredictionInfraRole
export AWS_POLICY_NAME=GitHubActionsHorseRacingPredictionInfraPolicy
export LIGHTSAIL_DOMAIN_NAME=api.example.com
export LIGHTSAIL_ACME_EMAIL=admin@example.com
export LIGHTSAIL_USERNAME=ubuntu
```

## 1. Terraform state 用 S3 バケットを作成する

```bash
aws s3api create-bucket \
  --bucket "$TF_STATE_BUCKET" \
  --region "$TF_STATE_REGION" \
```

```bash
aws s3api put-bucket-versioning \
  --bucket "$TF_STATE_BUCKET" \
  --versioning-configuration Status=Enabled

aws s3api put-bucket-encryption \
  --bucket "$TF_STATE_BUCKET" \
  --server-side-encryption-configuration '{
    "Rules": [
      {
        "ApplyServerSideEncryptionByDefault": {
          "SSEAlgorithm": "AES256"
        }
      }
    ]
  }'

aws s3api put-public-access-block \
  --bucket "$TF_STATE_BUCKET" \
  --public-access-block-configuration \
    BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
```

## 2. GitHub OIDC Provider と IAM ロールを作成する

```bash
aws iam create-open-id-connect-provider \
  --url https://token.actions.githubusercontent.com \
  --client-id-list sts.amazonaws.com \
  --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1
```

作成済み確認:

```bash
aws iam list-open-id-connect-providers
```

trust policy を作成します。

```bash
cat > /tmp/github-oidc-trust-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::${AWS_ACCOUNT_ID}:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": [
            "repo:${GITHUB_OWNER}/${GITHUB_REPO}:ref:refs/heads/${GITHUB_BRANCH}",
            "repo:${GITHUB_OWNER}/${GITHUB_REPO}:pull_request",
            "repo:${GITHUB_OWNER}/${GITHUB_REPO}:environment:${GITHUB_ENVIRONMENT}"
          ]
        }
      }
    }
  ]
}
EOF
```

ロールと policy を作成して付与します。

```bash
aws iam create-role \
  --role-name "$AWS_ROLE_NAME" \
  --assume-role-policy-document file:///tmp/github-oidc-trust-policy.json

cat > /tmp/github-actions-infra-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "LightsailAccess",
      "Effect": "Allow",
      "Action": [
        "lightsail:AllocateStaticIp",
        "lightsail:AttachStaticIp",
        "lightsail:CloseInstancePublicPorts",
        "lightsail:CreateInstances",
        "lightsail:CreateInstancesFromSnapshot",
        "lightsail:CreateKeyPair",
        "lightsail:DeleteInstance",
        "lightsail:DeleteKeyPair",
        "lightsail:DetachStaticIp",
        "lightsail:GetBlueprints",
        "lightsail:GetBundles",
        "lightsail:GetInstance",
        "lightsail:GetInstancePortStates",
        "lightsail:GetInstances",
        "lightsail:GetKeyPair",
        "lightsail:GetKeyPairs",
        "lightsail:GetOperation",
        "lightsail:GetOperations",
        "lightsail:GetStaticIp",
        "lightsail:GetStaticIps",
        "lightsail:ImportKeyPair",
        "lightsail:IsVpcPeered",
        "lightsail:PutInstancePublicPorts",
        "lightsail:OpenInstancePublicPorts",
        "lightsail:ReleaseStaticIp",
        "lightsail:TagResource",
        "lightsail:UpdateInstanceMetadataOptions"
      ],
      "Resource": "*"
    },
    {
      "Sid": "TerraformStateBucketAccess",
      "Effect": "Allow",
      "Action": [
        "s3:ListBucket",
        "s3:GetBucketVersioning"
      ],
      "Resource": "arn:aws:s3:::${TF_STATE_BUCKET}"
    },
    {
      "Sid": "TerraformStateObjectAccess",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject",
        "s3:DeleteObject"
      ],
      "Resource": "arn:aws:s3:::${TF_STATE_BUCKET}/*"
    }
  ]
}
EOF

aws iam create-policy \
  --policy-name "$AWS_POLICY_NAME" \
  --policy-document file:///tmp/github-actions-infra-policy.json

aws iam attach-role-policy \
  --role-name "$AWS_ROLE_NAME" \
  --policy-arn "arn:aws:iam::${AWS_ACCOUNT_ID}:policy/${AWS_POLICY_NAME}"
```

## 3. Lightsail 用 SSH 鍵を作成する

```bash
mkdir -p ~/.ssh
ssh-keygen -t ed25519 -C "horse-racing-prediction-lightsail" -f ~/.ssh/horse_racing_prediction_lightsail
```

必要なら確認します。

```bash
cat ~/.ssh/horse_racing_prediction_lightsail.pub
cat ~/.ssh/horse_racing_prediction_lightsail
```

## 4. GitHub Secrets を登録する

```bash
gh secret set AWS_ROLE_TO_ASSUME --body "arn:aws:iam::${AWS_ACCOUNT_ID}:role/${AWS_ROLE_NAME}"
gh secret set AWS_REGION --body "$AWS_REGION"
gh secret set AWS_AVAILABILITY_ZONE --body "${AWS_REGION}a"
gh secret set TF_STATE_BUCKET --body "$TF_STATE_BUCKET"
gh secret set TF_STATE_KEY --body "$TF_STATE_KEY"
gh secret set TF_STATE_REGION --body "$TF_STATE_REGION"
gh secret set LIGHTSAIL_SSH_PUBLIC_KEY < ~/.ssh/horse_racing_prediction_lightsail.pub
gh secret set LIGHTSAIL_SSH_PRIVATE_KEY < ~/.ssh/horse_racing_prediction_lightsail
gh secret set LIGHTSAIL_USERNAME --body "$LIGHTSAIL_USERNAME"
gh secret set LIGHTSAIL_DOMAIN_NAME --body "$LIGHTSAIL_DOMAIN_NAME"
gh secret set LIGHTSAIL_ACME_EMAIL --body "$LIGHTSAIL_ACME_EMAIL"
```

以下は値を決めてから登録します。

```bash
gh secret set LIGHTSAIL_API_KEY --body "$(openssl rand -base64 32)"
```

GHCR は public イメージ前提なので、`GHCR_USERNAME` と `GHCR_PAT` は不要です。

確認:

```bash
gh secret list
```

## 5. GitHub Environment を作成する

```bash
gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  "/repos/${GITHUB_OWNER}/${GITHUB_REPO}/environments/${GITHUB_ENVIRONMENT}"
```

## 6. infra-deploy workflow を実行する

```bash
gh workflow run infra-deploy.yml --ref "$GITHUB_BRANCH"
gh run list --workflow infra-deploy.yml --limit 5
gh run view --log
```

## 7. Static IP を控えて DNS を向ける

```bash
aws lightsail get-static-ips --query 'staticIps[].{Name:name,Ip:ipAddress,AttachedTo:attachedTo}' --output table
aws lightsail get-instances --query 'instances[].{Name:name,PublicIp:publicIpAddress,State:state.name}' --output table
```

表示された Static IP に公開ドメインの A レコードを向けます。

## 8. app workflow 用の残り Secrets を登録する

```bash
export LIGHTSAIL_HOST=$(aws lightsail get-static-ips --query 'staticIps[0].ipAddress' --output text)
gh secret set LIGHTSAIL_HOST --body "$LIGHTSAIL_HOST"
```

## 9. 初回 SSH 接続を確認する

```bash
ssh -i ~/.ssh/horse_racing_prediction_lightsail ${LIGHTSAIL_USERNAME}@${LIGHTSAIL_HOST}
```

接続後:

```bash
docker --version
docker compose version
ls -la /opt/horse-racing-prediction/app
ls -la /opt/horse-racing-prediction/app/data
exit
```

これらは [infra/lightsail/cloud-init.yaml.tftpl](../infra/lightsail/cloud-init.yaml.tftpl) の user data に依存しています。

## 10. app-deploy workflow を実行する

```bash
gh workflow run app-deploy.yml --ref "$GITHUB_BRANCH"
gh run list --workflow app-deploy.yml --limit 5
gh run view --log
```

初回は GitHub Packages の対象イメージを public に設定してください。package の visibility が private のままだと、Lightsail 側は認証なしで pull できません。

## 11. 動作確認をする

独自ドメインを登録していなくても、デプロイ時に `LIGHTSAIL_HOST`（静的IP）から `<IPをハイフン区切りにした文字列>.sslip.io` という形式のホスト名を自動的に導出し、Caddy がそのホスト名で Let's Encrypt の証明書を自動取得します。sslip.io はそのホスト名をそのまま埋め込まれたIPに解決するワイルドカードDNSサービスで、ドメインを購入・所有しなくても CA 発行の正規証明書を得られます。そのため `curl` や実クライアントは `-k` なしでそのまま接続できます。

```bash
export LIGHTSAIL_PUBLIC_HOSTNAME="$(echo "$LIGHTSAIL_HOST" | tr '.' '-').sslip.io"
curl -I https://${LIGHTSAIL_PUBLIC_HOSTNAME}/swagger/index.html
curl -I https://${LIGHTSAIL_PUBLIC_HOSTNAME}/swagger/v1/swagger.json
```

独自ドメインを別途用意している場合は、`LIGHTSAIL_DOMAIN_NAME`（と任意で `LIGHTSAIL_ACME_EMAIL`）を GitHub Secrets に設定すれば、そちらが優先されます（sslip.io は使われません）。

自己署名証明書での直接IPアクセス（デバッグ用）は引き続き有効です。

```bash
curl -k -I https://${LIGHTSAIL_HOST}/swagger/index.html
curl -k -I https://${LIGHTSAIL_HOST}/swagger/v1/swagger.json
```

API キー付きの確認例:

```bash
curl -H "X-Api-Key: <YOUR_API_KEY>" https://${LIGHTSAIL_PUBLIC_HOSTNAME}/api/races
curl -k -H "X-Api-Key: <YOUR_API_KEY>" https://${LIGHTSAIL_HOST}/api/races
```

## GitHub Secrets 一覧

### infra workflow 用

- `AWS_ROLE_TO_ASSUME`
- `AWS_REGION`
- `AWS_AVAILABILITY_ZONE`
- `TF_STATE_BUCKET`
- `TF_STATE_KEY`
- `TF_STATE_REGION`
- `LIGHTSAIL_SSH_PUBLIC_KEY`

### app workflow 用

- `LIGHTSAIL_HOST`
- `LIGHTSAIL_USERNAME`
- `LIGHTSAIL_SSH_PRIVATE_KEY`
- `LIGHTSAIL_DOMAIN_NAME` (独自ドメインを使う場合のみ。IP は設定しない)
- `LIGHTSAIL_ACME_EMAIL` (独自ドメインを使う場合のみ)
- `LIGHTSAIL_API_KEY`

## 運用メモ

- 独自ドメイン未設定時は、静的IPから導出した `sslip.io` ホスト名で Caddy が自動的に信頼された証明書を取得する（`.github/workflows/app-deploy.yml` 内で導出）
- API キーは `HORSE_RACING_API_KEY` としてコンテナへ注入される
- SQLite ファイルはサーバー上の `/opt/horse-racing-prediction/app/data/eventstore.db` に保存される
- バックアップは Lightsail のスナップショットで取得する
- `docker compose` の更新のみでアプリを入れ替えるため、インフラ workflow とアプリ workflow は独立している
- OIDC の引き受けはローカル CLI で再現しづらいため、最終確認は `infra-deploy` workflow の plan / apply で行う
