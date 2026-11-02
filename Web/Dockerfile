FROM node:22-alpine AS build
WORKDIR /web
COPY Web/package.json Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY Web/ ./
RUN npm run build

FROM nginx:1.30.6-alpine
COPY Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 80
