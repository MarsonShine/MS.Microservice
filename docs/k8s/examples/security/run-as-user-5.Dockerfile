# 镜像被配置为使⽤⽤户ID为5的⽤户运⾏
FROM node:7
ADD app.js /app.js
USER 5
ENTRYPOINT [ "node","app.js" ]